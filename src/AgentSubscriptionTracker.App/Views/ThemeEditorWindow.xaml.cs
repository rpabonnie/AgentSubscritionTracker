// SPEC-0004 §5.2 — theme editor code-behind: wires the in-memory ThemeEditorViewModel
// draft to typed controls, renders a live preview against a preview-scoped
// ResourceDictionary (PreviewContent.Resources only — never Application.Current.Resources),
// and pins/unpins the live callout's auto-hide watch (§4.4 edge case #16) for the duration
// the window is open. Sample preview data is hardcoded (never a live service call).

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using AgentSubscriptionTracker.App.Theming;
using AgentSubscriptionTracker.App.Tray;
using AgentSubscriptionTracker.App.ViewModels;

namespace AgentSubscriptionTracker.App.Views;

/// <summary>The theme editor window (SPEC-0004 §5.2). Not unit-tested — shell behavior
/// verified manually per SPEC-0004 §5/§6; the underlying <see cref="ThemeEditorViewModel"/>
/// save/import/contrast logic is unit-tested independently of this shell.</summary>
public sealed partial class ThemeEditorWindow : Window
{
    private readonly ThemeEditorViewModel _viewModel;
    private readonly IThemeFontResolver _fontResolver;
    private readonly CalloutController? _liveCallout;
    private bool _suppressFieldEvents;

    public ThemeEditorWindow(ThemeEditorViewModel viewModel, IThemeFontResolver fontResolver, CalloutController? liveCallout)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(fontResolver);

        _viewModel = viewModel;
        _fontResolver = fontResolver;
        _liveCallout = liveCallout;

        InitializeComponent();

        PopulateFontCombo(HeaderFontCombo);
        PopulateFontCombo(BodyFontCombo);
        PopulateFontCombo(FooterFontCombo);

        LoadFieldsFromViewModel();
        PreviewContent.DataContext = SamplePreviewData.Build();
        UpdatePreview();

        Loaded += (_, _) => _liveCallout?.SuspendAutoHide();
        Closed += (_, _) => _liveCallout?.ResumeAutoHide();
    }

    private static void PopulateFontCombo(System.Windows.Controls.ComboBox combo)
    {
        var names = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        combo.ItemsSource = names;
    }

    private void LoadFieldsFromViewModel()
    {
        _suppressFieldEvents = true;
        try
        {
            NameBox.Text = _viewModel.Name;
            FallbackColorBox.Text = _viewModel.BackgroundFallbackColor;
            ImagePathText.Text = _viewModel.BackgroundImagePath ?? "(none)";

            SetFontFields(HeaderFontCombo, HeaderFontSizeBox, _viewModel.Fonts.Header);
            SetFontFields(BodyFontCombo, BodyFontSizeBox, _viewModel.Fonts.Body);
            SetFontFields(FooterFontCombo, FooterFontSizeBox, _viewModel.Fonts.Footer);

            CalloutBackgroundBrushBox.Text = _viewModel.Brushes.CalloutBackgroundBrush;
            CalloutBorderBrushBox.Text = _viewModel.Brushes.CalloutBorderBrush;
            TextPrimaryBrushBox.Text = _viewModel.Brushes.TextPrimaryBrush;
            TextSecondaryBrushBox.Text = _viewModel.Brushes.TextSecondaryBrush;
            BarTrackBrushBox.Text = _viewModel.Brushes.BarTrackBrush;
            SeparatorBrushBox.Text = _viewModel.Brushes.SeparatorBrush;
            AccentBrushBox.Text = _viewModel.Brushes.AccentBrush;

            SeverityOkBox.Text = _viewModel.SeverityBands.Ok;
            SeverityWarnBox.Text = _viewModel.SeverityBands.Warn;
            SeverityCriticalBox.Text = _viewModel.SeverityBands.Critical;
        }
        finally
        {
            _suppressFieldEvents = false;
        }
    }

    private static void SetFontFields(
        System.Windows.Controls.ComboBox combo, System.Windows.Controls.TextBox sizeBox, ThemeFont font)
    {
        combo.SelectedItem = combo.Items.Cast<string>().FirstOrDefault(
            n => string.Equals(n, font.Family, StringComparison.OrdinalIgnoreCase));
        sizeBox.Text = font.Size.ToString(CultureInfo.InvariantCulture);
    }

    private void OnAnyFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressFieldEvents)
        {
            return;
        }

        ApplyFieldsToViewModel();
        UpdatePreview();
    }

    private void ApplyFieldsToViewModel()
    {
        _viewModel.Name = NameBox.Text;
        _viewModel.BackgroundFallbackColor = FallbackColorBox.Text;

        _viewModel.Fonts = new ThemeFontSet
        {
            Header = ReadFont(HeaderFontCombo, HeaderFontSizeBox, _viewModel.Fonts.Header),
            Body = ReadFont(BodyFontCombo, BodyFontSizeBox, _viewModel.Fonts.Body),
            Footer = ReadFont(FooterFontCombo, FooterFontSizeBox, _viewModel.Fonts.Footer),
        };

        _viewModel.Brushes = new ThemeBrushSet
        {
            CalloutBackgroundBrush = CalloutBackgroundBrushBox.Text,
            CalloutBorderBrush = CalloutBorderBrushBox.Text,
            TextPrimaryBrush = TextPrimaryBrushBox.Text,
            TextSecondaryBrush = TextSecondaryBrushBox.Text,
            BarTrackBrush = BarTrackBrushBox.Text,
            SeparatorBrush = SeparatorBrushBox.Text,
            AccentBrush = AccentBrushBox.Text,
        };

        _viewModel.SeverityBands = new ThemeSeverityBands
        {
            Ok = SeverityOkBox.Text,
            Warn = SeverityWarnBox.Text,
            Critical = SeverityCriticalBox.Text,
        };
    }

    private static ThemeFont ReadFont(
        System.Windows.Controls.ComboBox combo, System.Windows.Controls.TextBox sizeBox, ThemeFont previous)
    {
        var family = combo.SelectedItem as string ?? previous.Family;
        var size = double.TryParse(sizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : previous.Size;
        return previous with { Family = family, Size = size };
    }

    private void UpdatePreview()
    {
        ContrastWarningText.Text = _viewModel.LowContrastWarning ?? string.Empty;

        ThemeManifest manifest;
        try
        {
            manifest = _viewModel.BuildManifest();
        }
        catch (ArgumentException)
        {
            // Mid-typing intermediate state (e.g. an empty color box) — keep showing the
            // last good preview rather than throwing out of a TextChanged handler.
            return;
        }

        var previewTheme = new LoadedTheme
        {
            ThemeId = "preview",
            DisplayName = manifest.Name,
            Manifest = manifest,
            Status = ThemeLoadStatus.Ok,
            IsBuiltIn = false,
            BackgroundImage = null,
            FolderPath = string.Empty,
        };

        try
        {
            // Preview-scoped only (SPEC-0004 §5.2) — never Application.Current.Resources.
            ThemeResourceApplier.Apply(PreviewContent.Resources, previewTheme, _fontResolver);
        }
        catch (FormatException)
        {
            // An in-progress, not-yet-valid hex value while typing; keep the last good preview.
        }
    }

    private void OnPickImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PNG images (*.png)|*.png" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (_viewModel.TryImportBackgroundImage(dialog.FileName))
        {
            ImagePathText.Text = _viewModel.BackgroundImagePath ?? "(none)";
            ImportErrorText.Text = string.Empty;
        }
        else
        {
            ImportErrorText.Text = "Could not import the selected image.";
        }
    }

    private void OnClearImageClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearBackgroundImage();
        ImagePathText.Text = "(none)";
        ImportErrorText.Text = string.Empty;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ApplyFieldsToViewModel();
        _viewModel.Save();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        // Zero disk writes on Cancel (§5.2) — the draft is simply discarded.
        DialogResult = false;
        Close();
    }
}
