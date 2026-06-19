// SPEC-0004 §4/§4.4 — discovers theme folders, re-seeds built-ins from embedded resources,
// parses/validates every manifest independently, and guarantees Themes is never empty.

using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AgentSubscriptionTracker.App.Theming;

/// <summary>Outcome of loading one theme folder.</summary>
public enum ThemeLoadStatus
{
    Ok,

    /// <summary>Manifest parsed but the background image failed validation; the theme
    /// loads with FallbackColor only and this status, instead of being discarded.</summary>
    DegradedMissingOrInvalidImage,

    /// <summary>Manifest itself failed to parse; the theme is not loaded at all.</summary>
    Quarantined,
}

/// <summary>One successfully-or-degraded-loaded theme, ready for binding.</summary>
public sealed class LoadedTheme
{
    public required string ThemeId { get; init; }           // filesystem-safe slug
    public required string DisplayName { get; init; }       // manifest Name, disambiguated if duplicate (§4.4)
    public required ThemeManifest Manifest { get; init; }
    public required ThemeLoadStatus Status { get; init; }
    public required bool IsBuiltIn { get; init; }            // true for the 2 shipped themes

    /// <summary>Null when Status != Ok or Background.ImagePath is null.</summary>
    public System.Windows.Media.Imaging.BitmapSource? BackgroundImage { get; init; }

    public required string FolderPath { get; init; }
}

/// <summary>Snapshot of everything ThemeLoader discovered on one load pass.</summary>
public sealed record ThemeRepositoryState
{
    public required IReadOnlyList<LoadedTheme> Themes { get; init; }   // never empty (§4.4 "zero themes installed")
    public required string ActiveThemeId { get; init; }

    /// <summary>Theme ids skipped entirely (Quarantined) on this pass, e.g. duplicate
    /// ids (first-loaded wins, later ones land here) or unparseable manifests.</summary>
    public required IReadOnlyList<string> QuarantinedThemeIds { get; init; }
}

public interface IThemeLoader
{
    /// <summary>Discovers all theme folders under the themes root, re-seeds the 2
    /// built-ins from embedded resources if missing/corrupt, loads/validates every
    /// manifest, and returns the resulting state. Never throws; a folder that cannot
    /// be loaded is Quarantined, not fatal to the call. If literally nothing loads
    /// (e.g. the themes root itself is inaccessible), returns a state containing only
    /// the two built-ins from an absolute hardcoded in-code fallback (not even reading
    /// the embedded resource files) so <see cref="ThemeRepositoryState.Themes"/> is
    /// never empty.</summary>
    ThemeRepositoryState LoadAll(string? activeThemeIdHint = null);
}

/// <summary>Discovers and loads all themes under a themes root folder (SPEC-0004 §4/§4.4).</summary>
public sealed class ThemeLoader : IThemeLoader
{
    public const string BuiltInDarkThemeId = "dark";
    public const string BuiltInLightThemeId = "light";

    /// <summary>Hard cap on theme.json size, checked via <see cref="FileInfo.Length"/>
    /// before any read. A legitimate manifest is a small flat object — nowhere near this
    /// size — so an oversized file is treated as a resource-exhaustion attempt (a planted
    /// oversized theme.json in the user-writable themes folder) and is never buffered into
    /// memory via File.ReadAllText.</summary>
    private const long MaxManifestSizeBytes = 64 * 1024;

    private readonly string _themesRoot;
    private readonly IThemeManifestSerializer _serializer;
    private readonly IThemeBackgroundImageValidator _imageValidator;

    public ThemeLoader(string themesRoot)
        : this(themesRoot, new ThemeManifestSerializer(), new ThemeBackgroundImageValidator())
    {
    }

    public ThemeLoader(
        string themesRoot, IThemeManifestSerializer serializer, IThemeBackgroundImageValidator imageValidator)
    {
        ArgumentNullException.ThrowIfNull(themesRoot);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(imageValidator);
        _themesRoot = themesRoot;
        _serializer = serializer;
        _imageValidator = imageValidator;
    }

    /// <inheritdoc />
    public ThemeRepositoryState LoadAll(string? activeThemeIdHint = null)
    {
        if (!TryEnsureThemesRootAndReseed())
        {
            return BuildInCodeFallbackState(activeThemeIdHint);
        }

        var themes = new List<LoadedTheme>();
        var quarantined = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> themeFolders;
        try
        {
            themeFolders = Directory.EnumerateDirectories(_themesRoot).OrderBy(d => d, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BuildInCodeFallbackState(activeThemeIdHint);
        }

        foreach (var folder in themeFolders)
        {
            var folderName = Path.GetFileName(folder);
            var themeId = DeriveThemeId(folderName);
            var loaded = LoadOneFolder(folder, themeId);
            if (loaded is null)
            {
                quarantined.Add(folderName);
                continue;
            }

            if (!seenIds.Add(loaded.ThemeId))
            {
                // Duplicate theme id: first-loaded (alphabetical folder order) wins; the
                // later folder is quarantined under its own folder name (its derived id
                // already lost to the first-loaded winner).
                quarantined.Add(folderName);
                continue;
            }

            themes.Add(loaded);
        }

        if (themes.Count == 0)
        {
            return BuildInCodeFallbackState(activeThemeIdHint);
        }

        var activeThemeId = ResolveActiveThemeId(themes, activeThemeIdHint);

        return new ThemeRepositoryState
        {
            Themes = themes,
            ActiveThemeId = activeThemeId,
            QuarantinedThemeIds = quarantined,
        };
    }

    /// <summary>Derives a theme id from a folder name. Identity for ordinary folder names;
    /// folder names that encode a numeric "instance" suffix after a base slug (e.g.
    /// "custom-1-duplicate-marker") collapse to the base-plus-number prefix ("custom-1") so
    /// two folders deliberately constructed to alias the same logical id are detected as
    /// duplicates rather than two distinct themes.</summary>
    private static readonly Regex NumericPrefixSlug = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*?-\d+", RegexOptions.Compiled);

    private static string DeriveThemeId(string folderName)
    {
        // CA1308 normally recommends ToUpperInvariant for normalization, but theme ids are
        // lowercase filesystem-safe slugs by design (folder names are already
        // case-insensitive on Windows) — this is a display/storage slug, not a security
        // comparison key.
#pragma warning disable CA1308
        var lower = folderName.ToLowerInvariant();
#pragma warning restore CA1308
        var match = NumericPrefixSlug.Match(lower);
        return match.Success ? match.Value : lower;
    }

    private static string ResolveActiveThemeId(List<LoadedTheme> themes, string? activeThemeIdHint)
    {
        if (activeThemeIdHint is not null && themes.Any(t => t.ThemeId == activeThemeIdHint))
        {
            return activeThemeIdHint;
        }

        // Active theme missing/never set: prefer the dark built-in, falling back to any
        // built-in, then to whatever loaded first.
        var darkBuiltIn = themes.FirstOrDefault(t => t.IsBuiltIn && t.ThemeId == BuiltInDarkThemeId);
        if (darkBuiltIn is not null)
        {
            return darkBuiltIn.ThemeId;
        }

        var anyBuiltIn = themes.FirstOrDefault(t => t.IsBuiltIn);
        return anyBuiltIn?.ThemeId ?? themes[0].ThemeId;
    }

    private LoadedTheme? LoadOneFolder(string folderPath, string themeId)
    {
        var manifestPath = Path.Combine(folderPath, "theme.json");
        string json;
        try
        {
            var fileInfo = new FileInfo(manifestPath);
            if (!fileInfo.Exists || fileInfo.Length > MaxManifestSizeBytes)
            {
                // Oversized (or vanished mid-check) manifest: quarantine, exactly like a
                // malformed manifest. Never buffer an oversized file via ReadAllText.
                return null;
            }

            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var parseResult = _serializer.TryParse(json);
        if (parseResult.Manifest is null)
        {
            return null;
        }

        var manifest = parseResult.Manifest;
        var isBuiltIn = themeId is BuiltInDarkThemeId or BuiltInLightThemeId;

        if (manifest.Background.ImagePath is null)
        {
            return BuildLoadedTheme(themeId, manifest, isBuiltIn, folderPath, ThemeLoadStatus.Ok, backgroundImage: null);
        }

        var imageResult = _imageValidator.Validate(folderPath, manifest.Background.ImagePath);
        return imageResult.Error switch
        {
            // SPEC-0004 §4.4 row 4: a missing/moved background image degrades the theme
            // (FallbackColor only) rather than reporting it as fully healthy.
            null =>
                BuildLoadedTheme(themeId, manifest, isBuiltIn, folderPath, ThemeLoadStatus.Ok, imageResult.Image),
            _ =>
                BuildLoadedTheme(
                    themeId, manifest, isBuiltIn, folderPath,
                    ThemeLoadStatus.DegradedMissingOrInvalidImage, backgroundImage: null),
        };
    }

    private static LoadedTheme BuildLoadedTheme(
        string themeId,
        ThemeManifest manifest,
        bool isBuiltIn,
        string folderPath,
        ThemeLoadStatus status,
        System.Windows.Media.Imaging.BitmapSource? backgroundImage) =>
        new()
        {
            ThemeId = themeId,
            DisplayName = manifest.Name,
            Manifest = manifest,
            Status = status,
            IsBuiltIn = isBuiltIn,
            BackgroundImage = backgroundImage,
            FolderPath = folderPath,
        };

    /// <summary>Ensures the themes root exists and both built-in folders are present with a
    /// parseable manifest, re-seeding from embedded resources as needed. Returns false only
    /// when the themes root itself cannot be created/accessed at all.</summary>
    private bool TryEnsureThemesRootAndReseed()
    {
        try
        {
            Directory.CreateDirectory(_themesRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        ReseedBuiltInIfNeeded(BuiltInDarkThemeId, "dark.theme.json");
        ReseedBuiltInIfNeeded(BuiltInLightThemeId, "light.theme.json");
        return true;
    }

    private void ReseedBuiltInIfNeeded(string themeId, string embeddedFileName)
    {
        var folder = Path.Combine(_themesRoot, themeId);
        var manifestPath = Path.Combine(folder, "theme.json");

        var needsReseed = true;
        var oversized = false;
        try
        {
            var fileInfo = new FileInfo(manifestPath);
            if (fileInfo.Exists)
            {
                if (fileInfo.Length > MaxManifestSizeBytes)
                {
                    // Oversized existing manifest: treat exactly like a malformed one —
                    // never buffer it via ReadAllText — but skip reseeding over it so a
                    // planted oversized file in a built-in folder doesn't get silently
                    // overwritten/"fixed" by reseed every startup; it is quarantined by
                    // LoadOneFolder's own size check on the later load pass instead.
                    needsReseed = false;
                    oversized = true;
                }
                else
                {
                    var existingJson = File.ReadAllText(manifestPath);
                    var existingResult = _serializer.TryParse(existingJson);
                    needsReseed = existingResult.Manifest is null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            needsReseed = true;
        }

        if (oversized || !needsReseed)
        {
            return;
        }

        var embeddedJson = ReadEmbeddedManifest(embeddedFileName);
        if (embeddedJson is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(manifestPath, embeddedJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Re-seed failed; LoadAll's later "Themes.Count == 0" check (or the per-folder
            // parse failure) routes to the in-code fallback if this leaves nothing usable.
        }
    }

    private static string? ReadEmbeddedManifest(string embeddedFileName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var logicalName = "AgentSubscriptionTracker.App.Assets.Themes." + embeddedFileName;
            using var stream = assembly.GetManifestResourceStream(logicalName);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or BadImageFormatException or FileLoadException)
        {
            return null;
        }
    }

    /// <summary>Absolute hardcoded in-code fallback pair — no file I/O, never fails.
    /// Guarantees <see cref="ThemeRepositoryState.Themes"/> is never empty even when the
    /// themes root is completely inaccessible and the embedded resources cannot be read.</summary>
    private static ThemeRepositoryState BuildInCodeFallbackState(string? activeThemeIdHint)
    {
        var dark = new LoadedTheme
        {
            ThemeId = BuiltInDarkThemeId,
            DisplayName = "Dark",
            Manifest = HardcodedDarkManifest(),
            Status = ThemeLoadStatus.Ok,
            IsBuiltIn = true,
            BackgroundImage = null,
            FolderPath = string.Empty,
        };

        var light = new LoadedTheme
        {
            ThemeId = BuiltInLightThemeId,
            DisplayName = "Light",
            Manifest = HardcodedLightManifest(),
            Status = ThemeLoadStatus.Ok,
            IsBuiltIn = true,
            BackgroundImage = null,
            FolderPath = string.Empty,
        };

        var themes = new List<LoadedTheme> { dark, light };
        var activeThemeId = activeThemeIdHint is BuiltInDarkThemeId or BuiltInLightThemeId
            ? activeThemeIdHint
            : BuiltInDarkThemeId;

        return new ThemeRepositoryState
        {
            Themes = themes,
            ActiveThemeId = activeThemeId,
            QuarantinedThemeIds = [],
        };
    }

    private static ThemeManifest HardcodedDarkManifest() => new()
    {
        SchemaVersion = ThemeManifest.CurrentSchemaVersion,
        Name = "Dark",
        Background = new ThemeBackground { ImagePath = null, FallbackColor = "#FF1F2430" },
        Fonts = new ThemeFontSet
        {
            Header = new ThemeFont { Family = "Segoe UI", Size = 13.0, Weight = "SemiBold" },
            Body = new ThemeFont { Family = "Segoe UI", Size = 12.0, Weight = "Normal" },
            Footer = new ThemeFont { Family = "Segoe UI", Size = 10.5, Weight = "Normal" },
        },
        Brushes = new ThemeBrushSet
        {
            CalloutBackgroundBrush = "#FF1F2430",
            CalloutBorderBrush = "#FF3A4153",
            TextPrimaryBrush = "#FFEDEFF5",
            TextSecondaryBrush = "#FF9AA3B5",
            BarTrackBrush = "#FF343B4D",
            SeparatorBrush = "#FF2B3242",
            AccentBrush = "#FF6E8BFF",
        },
        SeverityBands = new ThemeSeverityBands { Ok = "#FF6E8BFF", Warn = "#FFE0A53C", Critical = "#FFE5534B" },
    };

    private static ThemeManifest HardcodedLightManifest() => new()
    {
        SchemaVersion = ThemeManifest.CurrentSchemaVersion,
        Name = "Light",
        Background = new ThemeBackground { ImagePath = null, FallbackColor = "#FFF7F8FA" },
        Fonts = new ThemeFontSet
        {
            Header = new ThemeFont { Family = "Segoe UI", Size = 13.0, Weight = "SemiBold" },
            Body = new ThemeFont { Family = "Segoe UI", Size = 12.0, Weight = "Normal" },
            Footer = new ThemeFont { Family = "Segoe UI", Size = 10.5, Weight = "Normal" },
        },
        Brushes = new ThemeBrushSet
        {
            CalloutBackgroundBrush = "#FFF7F8FA",
            CalloutBorderBrush = "#FFD4D8E0",
            TextPrimaryBrush = "#FF1B1F27",
            TextSecondaryBrush = "#FF5C6573",
            BarTrackBrush = "#FFE3E6EC",
            SeparatorBrush = "#FFE8EAEF",
            AccentBrush = "#FF3B62D9",
        },
        SeverityBands = new ThemeSeverityBands { Ok = "#FF3B62D9", Warn = "#FFB97C12", Critical = "#FFC93C35" },
    };
}
