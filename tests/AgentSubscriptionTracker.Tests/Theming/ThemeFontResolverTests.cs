// SPEC-0004 §4/§7 — font family resolution against installed system fonts, with
// graceful fallback to Segoe UI and never-throw semantics.
// Spec-phase stub: references AgentSubscriptionTracker.App.Theming types that do not
// exist yet, so the test project intentionally does not compile until TASK-016
// implements SPEC-0004.

using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemeFontResolverTests
{
    private static IThemeFontResolver CreateResolver() => new ThemeFontResolver();

    [Fact]
    public void Resolve_KnownInstalledFamily_ResolvesWithoutFallback()
    {
        var resolver = CreateResolver();
        var font = new ThemeFont { Family = "Segoe UI", Size = 12.0, Weight = "Normal" };

        var resolved = resolver.Resolve(font);

        Assert.False(resolved.FellBackToDefault);
        Assert.Equal("Segoe UI", resolved.Family.Source);
        Assert.Equal(12.0, resolved.Size);
    }

    [Fact]
    public void Resolve_UnknownFamily_FallsBackToSegoeUi_AndNeverThrows()
    {
        var resolver = CreateResolver();
        var font = new ThemeFont { Family = "Totally Not A Real Font Family XYZ", Size = 12.0, Weight = "Normal" };

        var resolved = resolver.Resolve(font);

        Assert.True(resolved.FellBackToDefault);
        Assert.Equal("Segoe UI", resolved.Family.Source);
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("SemiBold")]
    [InlineData("Bold")]
    [InlineData("Light")]
    [InlineData("normal")]
    [InlineData("SEMIBOLD")]
    public void Resolve_KnownWeightStrings_MapToExpectedFontWeight_CaseInsensitive(string weight)
    {
        var resolver = CreateResolver();
        var font = new ThemeFont { Family = "Segoe UI", Size = 12.0, Weight = weight };

        var resolved = resolver.Resolve(font);

        var expected = weight.ToLowerInvariant() switch
        {
            "normal" => System.Windows.FontWeights.Normal,
            "semibold" => System.Windows.FontWeights.SemiBold,
            "bold" => System.Windows.FontWeights.Bold,
            "light" => System.Windows.FontWeights.Light,
            _ => throw new InvalidOperationException("unreachable for this theory's data"),
        };
        Assert.Equal(expected, resolved.Weight);
    }

    [Fact]
    public void Resolve_UnknownWeightString_MapsToNormal_WithoutAffectingFellBackToDefault()
    {
        var resolver = CreateResolver();
        var font = new ThemeFont { Family = "Segoe UI", Size = 12.0, Weight = "UltraMegaBold" };

        var resolved = resolver.Resolve(font);

        Assert.Equal(System.Windows.FontWeights.Normal, resolved.Weight);
        Assert.False(resolved.FellBackToDefault); // family itself was valid — only weight was unknown
    }

    [Fact]
    public void Resolve_FixtureWithUnknownFontFamily_FallsBackGracefully()
    {
        var resolver = CreateResolver();
        var json = ThemeTestSupport.ReadFixtureText("theme_unknown_font_family.json");
        // This stub asserts the resolver's contract directly; TASK-016's
        // ThemeLoader/ThemeManifestSerializer integration test (ThemeLoaderTests)
        // covers the end-to-end "manifest with unknown font still loads" path.
        Assert.Contains("Totally Not A Real Font Family XYZ", json);

        var resolved = resolver.Resolve(new ThemeFont { Family = "Totally Not A Real Font Family XYZ", Size = 13.0, Weight = "SemiBold" });

        Assert.True(resolved.FellBackToDefault);
    }
}
