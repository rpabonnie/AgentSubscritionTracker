// SPEC-0004 §4/§4.1/§7 — ThemeManifest strict, bounded JSON (de)serialization.
// Spec-phase stub: references AgentSubscriptionTracker.App.Theming types that do not
// exist yet, so the test project intentionally does not compile until TASK-016
// implements SPEC-0004.

using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemeManifestSerializerTests
{
    private static IThemeManifestSerializer CreateSerializer() => new ThemeManifestSerializer();

    [Fact]
    public void TryParse_ValidDarkFixture_ReturnsManifestWithNoError()
    {
        var serializer = CreateSerializer();
        var json = ThemeTestSupport.ReadFixtureText("theme_valid_dark.json");

        var result = serializer.TryParse(json);

        Assert.Null(result.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal("Dark", result.Manifest!.Name);
        Assert.Equal(ThemeManifest.CurrentSchemaVersion, result.Manifest.SchemaVersion);
        Assert.Equal("background.png", result.Manifest.Background.ImagePath);
        Assert.Equal("#FF1F2430", result.Manifest.Background.FallbackColor);
    }

    [Fact]
    public void TryParse_ValidLightFixture_ReturnsManifestWithNullImagePath()
    {
        var serializer = CreateSerializer();
        var json = ThemeTestSupport.ReadFixtureText("theme_valid_light.json");

        var result = serializer.TryParse(json);

        Assert.Null(result.Error);
        Assert.NotNull(result.Manifest);
        Assert.Null(result.Manifest!.Background.ImagePath);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsInvalidJsonAndNullManifest()
    {
        var serializer = CreateSerializer();
        var json = ThemeTestSupport.ReadFixtureText("theme_malformed_json.json");

        var result = serializer.TryParse(json);

        Assert.Equal(ThemeManifestParseError.InvalidJson, result.Error);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void TryParse_MissingRequiredField_ReturnsMissingRequiredFieldAndNullManifest()
    {
        var serializer = CreateSerializer();
        var json = ThemeTestSupport.ReadFixtureText("theme_missing_required_field.json");

        var result = serializer.TryParse(json);

        Assert.Equal(ThemeManifestParseError.MissingRequiredField, result.Error);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void TryParse_InvalidColorSyntax_QuarantinesWholeFile_NeverClampsSingleValue()
    {
        var serializer = CreateSerializer();
        var json = ThemeTestSupport.ReadFixtureText("theme_out_of_range_color.json");

        var result = serializer.TryParse(json);

        Assert.Equal(ThemeManifestParseError.InvalidColorFormat, result.Error);
        Assert.Null(result.Manifest); // whole-file failure, not a partially-loaded manifest with a substituted color
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(97.0)]
    [InlineData(double.NaN)]
    public void TryParse_OutOfRangeFontSize_ReturnsOutOfRangeValue(double badSize)
    {
        var serializer = CreateSerializer();
        var manifest = ThemeTestSupport.ValidDarkManifest() with
        {
            Fonts = ThemeTestSupport.ValidDarkManifest().Fonts with
            {
                Body = new ThemeFont { Family = "Segoe UI", Size = badSize, Weight = "Normal" },
            },
        };
        var json = serializer.Serialize(manifest);

        var result = serializer.TryParse(json);

        Assert.Equal(ThemeManifestParseError.OutOfRangeValue, result.Error);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void TryParse_UnsupportedSchemaVersion_IsRejected()
    {
        var serializer = CreateSerializer();
        var manifest = ThemeTestSupport.ValidDarkManifest() with { SchemaVersion = 999 };
        var json = serializer.Serialize(manifest);
        // Serialize() may legitimately write whatever SchemaVersion is set on the
        // record (the editor/loader is the layer that pins CurrentSchemaVersion on
        // write); re-parsing an out-of-support version must still be rejected.

        var result = serializer.TryParse(json);

        Assert.Equal(ThemeManifestParseError.UnsupportedSchemaVersion, result.Error);
        Assert.Null(result.Manifest);
    }

    [Theory]
    [InlineData("theme_path_traversal.json")]
    [InlineData("theme_absolute_path.json")]
    public void TryParse_TraversalOrAbsoluteImagePath_RejectedBeforeTouchingFilesystem(string fixtureFile)
    {
        var serializer = CreateSerializer();
        var json = ThemeTestSupport.ReadFixtureText(fixtureFile);

        var result = serializer.TryParse(json);

        Assert.Equal(ThemeManifestParseError.PathTraversalRejected, result.Error);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void TryParse_UnknownJsonProperties_AreIgnoredNotRejected()
    {
        var serializer = CreateSerializer();
        var json = ThemeTestSupport.ReadFixtureText("theme_valid_dark.json")
            .Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"futureField\": { \"nested\": true },");

        var result = serializer.TryParse(json);

        Assert.Null(result.Error);
        Assert.NotNull(result.Manifest);
    }

    [Fact]
    public void Serialize_ThenTryParse_RoundTripsDarkManifest()
    {
        var serializer = CreateSerializer();
        var original = ThemeTestSupport.ValidDarkManifest();

        var json = serializer.Serialize(original);
        var result = serializer.TryParse(json);

        Assert.Null(result.Error);
        Assert.Equal(original, result.Manifest);
    }

    [Fact]
    public void Serialize_ThenTryParse_RoundTripsLightManifestWithNullImagePath()
    {
        var serializer = CreateSerializer();
        var original = ThemeTestSupport.ValidLightManifestNoImage();

        var json = serializer.Serialize(original);
        var result = serializer.TryParse(json);

        Assert.Null(result.Error);
        Assert.Equal(original, result.Manifest);
    }
}
