// SPEC-0004 §5.2 — backs ThemeEditorWindow: in-memory draft manifest, background image
// import (reusing the loader's validation pipeline), low-contrast warning, and the
// Save (validate -> persist via IThemeStore -> live-update if active) / Cancel (zero disk
// writes) pipeline. Operates on an in-memory copy from the moment it opens (§4.4 edge case
// #9 — source file deleted/renamed mid-edit never corrupts a save, it falls through to
// Save-As).

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>Carries the theme id persisted by a successful <see cref="ThemeEditorViewModel.Save"/>.</summary>
public sealed class ThemeSavedEventArgs(string themeId) : EventArgs
{
    public string ThemeId { get; } = themeId;
}

/// <summary>Mutable in-memory draft of a theme manifest plus optional new background image
/// bytes, edited by <see cref="ThemeEditorViewModel"/>. Never written to disk until Save.</summary>
public sealed class ThemeEditorViewModel : INotifyPropertyChanged
{
    private readonly IThemeStore _store;
    private readonly IThemeFontResolver _fontResolver;
    private readonly IThemeBackgroundImageValidator _imageValidator;
    private readonly string? _sourceThemeId;
    private readonly bool _sourceIsBuiltIn;
    private readonly string? _sourceFolderPath;

    private string _name;
    private string? _backgroundImagePath;
    private byte[]? _pendingBackgroundPngBytes;
    private string _backgroundFallbackColor;
    private ThemeFontSet _fonts;
    private ThemeBrushSet _brushes;
    private ThemeSeverityBands _severityBands;
    private string? _importError;

    public ThemeEditorViewModel(
        ThemeManifest seed,
        IThemeStore store,
        IThemeFontResolver fontResolver,
        IThemeBackgroundImageValidator imageValidator,
        string? sourceThemeId,
        bool sourceIsBuiltIn,
        string? sourceFolderPath)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fontResolver);
        ArgumentNullException.ThrowIfNull(imageValidator);

        _store = store;
        _fontResolver = fontResolver;
        _imageValidator = imageValidator;
        _sourceThemeId = sourceThemeId;
        _sourceIsBuiltIn = sourceIsBuiltIn;
        _sourceFolderPath = sourceFolderPath;

        _name = seed.Name;
        _backgroundImagePath = seed.Background.ImagePath;
        _backgroundFallbackColor = seed.Background.FallbackColor;
        _fonts = seed.Fonts;
        _brushes = seed.Brushes;
        _severityBands = seed.SeverityBands;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after a successful Save with the new/updated theme id, so the shell
    /// can refresh the picker and, if the saved id is the active theme, live-update the
    /// running callout (§5.2).</summary>
    public event EventHandler<ThemeSavedEventArgs>? Saved;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string BackgroundFallbackColor
    {
        get => _backgroundFallbackColor;
        set => SetField(ref _backgroundFallbackColor, value);
    }

    public string? BackgroundImagePath => _backgroundImagePath;

    public ThemeFontSet Fonts
    {
        get => _fonts;
        set => SetField(ref _fonts, value);
    }

    public ThemeBrushSet Brushes
    {
        get => _brushes;
        set => SetField(ref _brushes, value);
    }

    public ThemeSeverityBands SeverityBands
    {
        get => _severityBands;
        set => SetField(ref _severityBands, value);
    }

    /// <summary>Non-null when the most recent background-image import attempt failed
    /// (§4.4 edge case #20); never blocks Save, purely informational.</summary>
    public string? ImportError
    {
        get => _importError;
        private set => SetField(ref _importError, value);
    }

    /// <summary>Non-blocking low-contrast warning (§4.4 edge case #18). Null when the
    /// contrast ratio between TextPrimaryBrush/TextSecondaryBrush and
    /// CalloutBackgroundBrush is acceptable.</summary>
    public string? LowContrastWarning => ComputeLowContrastWarning();

    /// <summary>Imports a candidate background image file, reusing the same validation
    /// pipeline the loader uses (§4.4 edge case #20). On success, stages the bytes for Save
    /// and updates <see cref="BackgroundImagePath"/>; on failure, sets
    /// <see cref="ImportError"/> and leaves the draft's previous background state
    /// untouched — never throws, never corrupts the on-disk theme.</summary>
    public bool TryImportBackgroundImage(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ImportError = "Could not read the selected file.";
            return false;
        }

        // Validate against a temporary staging folder so the same containment/decode/size/
        // alpha pipeline used at load time also gates import-time acceptance.
        var stagingDir = Path.Combine(Path.GetTempPath(), "AgentSubscriptionTracker.ThemeImport." + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingDir);
            var stagedFile = Path.Combine(stagingDir, "candidate.png");
            File.WriteAllBytes(stagedFile, bytes);

            var result = _imageValidator.Validate(stagingDir, "candidate.png");
            if (result.Error is not null)
            {
                ImportError = DescribeImportError(result.Error.Value);
                return false;
            }

            _pendingBackgroundPngBytes = bytes;
            _backgroundImagePath = "background.png";
            ImportError = null;
            OnPropertyChanged(nameof(BackgroundImagePath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ImportError = "Could not import the selected file.";
            return false;
        }
        finally
        {
            TryDeleteStaging(stagingDir);
        }
    }

    /// <summary>Clears the background image; the theme falls back to
    /// <see cref="BackgroundFallbackColor"/> only.</summary>
    public void ClearBackgroundImage()
    {
        _backgroundImagePath = null;
        _pendingBackgroundPngBytes = null;
        ImportError = null;
        OnPropertyChanged(nameof(BackgroundImagePath));
    }

    /// <summary>Validates and persists the draft. Built-ins always route through
    /// <see cref="IThemeStore.SaveAsNew"/> (§4.4 edge case #13); a non-built-in whose
    /// original folder no longer exists also routes through SaveAsNew rather than failing
    /// (§4.4 edge case #9). Returns the persisted theme id.</summary>
    public string Save()
    {
        var manifest = BuildManifest();
        var sourceFolderStillExists = _sourceFolderPath is not null && Directory.Exists(_sourceFolderPath);

        string themeId;
        if (_sourceThemeId is not { } existingThemeId || _sourceIsBuiltIn || !sourceFolderStillExists)
        {
            themeId = _store.SaveAsNew(manifest, _pendingBackgroundPngBytes);
        }
        else
        {
            _store.Overwrite(existingThemeId, manifest, _pendingBackgroundPngBytes);
            themeId = existingThemeId;
        }

        Saved?.Invoke(this, new ThemeSavedEventArgs(themeId));
        return themeId;
    }

    /// <summary>Builds the manifest the draft currently represents, without persisting it.</summary>
    public ThemeManifest BuildManifest() => new()
    {
        SchemaVersion = ThemeManifest.CurrentSchemaVersion,
        Name = Name,
        Background = new ThemeBackground { ImagePath = BackgroundImagePath, FallbackColor = BackgroundFallbackColor },
        Fonts = Fonts,
        Brushes = Brushes,
        SeverityBands = SeverityBands,
    };

    private string? ComputeLowContrastWarning()
    {
        const double MinimumContrastRatio = 3.0;

        if (!TryParseColor(Brushes.CalloutBackgroundBrush, out var background))
        {
            return null;
        }

        if (TryParseColor(Brushes.TextPrimaryBrush, out var primary)
            && ContrastRatio(primary, background) < MinimumContrastRatio)
        {
            return "Primary text has low contrast against the background.";
        }

        if (TryParseColor(Brushes.TextSecondaryBrush, out var secondary)
            && ContrastRatio(secondary, background) < MinimumContrastRatio)
        {
            return "Secondary text has low contrast against the background.";
        }

        return null;
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Falls through to the failure return below.
        }

        color = default;
        return false;
    }

    /// <summary>WCAG-style relative luminance contrast ratio.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        var luminanceA = RelativeLuminance(a) + 0.05;
        var luminanceB = RelativeLuminance(b) + 0.05;
        return luminanceA > luminanceB ? luminanceA / luminanceB : luminanceB / luminanceA;
    }

    private static double RelativeLuminance(Color color)
    {
        double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static string DescribeImportError(BackgroundImageValidationError error) => error switch
    {
        BackgroundImageValidationError.NotAPng => "The selected file is not a PNG image.",
        BackgroundImageValidationError.CorruptOrTruncated => "The selected PNG could not be decoded.",
        BackgroundImageValidationError.FileTooLarge => "The selected file exceeds the 2 MB size limit.",
        BackgroundImageValidationError.DimensionsTooLarge => "The selected image exceeds 1024x1536 pixels.",
        BackgroundImageValidationError.NoAlphaChannel => "The selected PNG has no alpha channel.",
        BackgroundImageValidationError.FileNotFound => "The selected file could not be found.",
        BackgroundImageValidationError.PathOutsideThemeFolder => "The selected file path is not valid.",
        _ => "The selected image could not be imported.",
    };

    private static void TryDeleteStaging(string stagingDir)
    {
        try
        {
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temp staging folder; never surfaced to the user.
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
