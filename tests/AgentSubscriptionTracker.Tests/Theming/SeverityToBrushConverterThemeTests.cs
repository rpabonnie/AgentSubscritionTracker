// SPEC-0004 §5.3/§7 — SeverityToBrushConverter resolves ok/warn/critical/BarTrackBrush
// from the active theme's DynamicResources instead of static converter-owned colors.
// Spec-phase stub: references AgentSubscriptionTracker.App.Views.SeverityToBrushConverter
// (extended by SPEC-0004) and AgentSubscriptionTracker.App.ViewModels.UsageSeverity
// (existing, SPEC-0003). The test project intentionally does not compile until
// TASK-016 implements SPEC-0004's modification to that converter.

using System.Windows;
using System.Windows.Media;
using AgentSubscriptionTracker.App.ViewModels;
using AgentSubscriptionTracker.App.Views;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class SeverityToBrushConverterThemeTests
{
    private static FrameworkElement CreateElementWithThemeResources(
        Color ok, Color warn, Color critical, Color barTrack)
    {
        var element = new FrameworkElement();
        element.Resources["ok"] = new SolidColorBrush(ok);
        element.Resources["warn"] = new SolidColorBrush(warn);
        element.Resources["critical"] = new SolidColorBrush(critical);
        element.Resources["BarTrackBrush"] = new SolidColorBrush(barTrack);
        return element;
    }

    [Fact]
    public void Convert_NormalSeverity_ResolvesOkResourceFromActiveScope() => ThemeTestSupport.RunOnSta(() =>
    {
        var converter = new SeverityToBrushConverter();
        var ok = Color.FromRgb(0x35, 0xC4, 0x6B);
        var element = CreateElementWithThemeResources(ok, Colors.Orange, Colors.Red, Colors.Gray);

        var result = converter.Convert(UsageSeverity.Normal, typeof(Brush), element, System.Globalization.CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(ok, brush.Color);
    });

    [Fact]
    public void Convert_WarningSeverity_ResolvesWarnResourceFromActiveScope() => ThemeTestSupport.RunOnSta(() =>
    {
        var converter = new SeverityToBrushConverter();
        var warn = Color.FromRgb(0xE0, 0xA2, 0x2B);
        var element = CreateElementWithThemeResources(Colors.Green, warn, Colors.Red, Colors.Gray);

        var result = converter.Convert(UsageSeverity.Warning, typeof(Brush), element, System.Globalization.CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(warn, brush.Color);
    });

    [Fact]
    public void Convert_CriticalSeverity_ResolvesCriticalResourceFromActiveScope() => ThemeTestSupport.RunOnSta(() =>
    {
        var converter = new SeverityToBrushConverter();
        var critical = Color.FromRgb(0xE0, 0x5C, 0x5C);
        var element = CreateElementWithThemeResources(Colors.Green, Colors.Orange, critical, Colors.Gray);

        var result = converter.Convert(UsageSeverity.Critical, typeof(Brush), element, System.Globalization.CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(critical, brush.Color);
    });

    [Fact]
    public void Convert_SameConverterInstance_ReflectsResourceSwap_WithoutReconstruction() => ThemeTestSupport.RunOnSta(() =>
    {
        // Edge case #17 (rapid theme switching): the converter must not cache a
        // resolved brush per-instance — swapping the element's resource dictionary
        // (simulating an idempotent theme swap) must change subsequent outputs.
        var converter = new SeverityToBrushConverter();
        var firstOk = Color.FromRgb(0x35, 0xC4, 0x6B);
        var element = CreateElementWithThemeResources(firstOk, Colors.Orange, Colors.Red, Colors.Gray);

        var first = (SolidColorBrush)converter.Convert(UsageSeverity.Normal, typeof(Brush), element, System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal(firstOk, first.Color);

        var secondOk = Color.FromRgb(0x1F, 0x9D, 0x52);
        element.Resources["ok"] = new SolidColorBrush(secondOk);

        var second = (SolidColorBrush)converter.Convert(UsageSeverity.Normal, typeof(Brush), element, System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal(secondOk, second.Color);
    });

    [Fact]
    public void ResolveBarTrackBrush_FromActiveScope_MatchesThemeResource() => ThemeTestSupport.RunOnSta(() =>
    {
        var converter = new SeverityToBrushConverter();
        var barTrack = Color.FromRgb(0x2C, 0x31, 0x3D);
        var element = CreateElementWithThemeResources(Colors.Green, Colors.Orange, Colors.Red, barTrack);

        var resolved = converter.ResolveBarTrackBrush(element);

        var brush = Assert.IsType<SolidColorBrush>(resolved);
        Assert.Equal(barTrack, brush.Color);
    });
}
