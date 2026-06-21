// SPEC-0004 §4.3/§7 — background image validation pipeline: format, alpha, size,
// dimension checks, ordering, and fallback (degrade, never crash/quarantine).
// Spec-phase stub: references AgentSubscriptionTracker.App.Theming types that do not
// exist yet, so the test project intentionally does not compile until TASK-016
// implements SPEC-0004.

using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemeBackgroundImageValidatorTests
{
    private static IThemeBackgroundImageValidator CreateValidator() => new ThemeBackgroundImageValidator();

    private static string FixturesThemeFolder() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Theming");

    [Fact]
    public void Validate_ValidPngWithRealAlpha_Succeeds()
    {
        var validator = CreateValidator();

        var result = validator.Validate(FixturesThemeFolder(), "valid_with_alpha_340x480.png");

        Assert.Null(result.Error);
        Assert.NotNull(result.Image);
    }

    [Fact]
    public void Validate_ValidPngDifferentAspect_StillWithinCaps_Succeeds()
    {
        var validator = CreateValidator();

        var result = validator.Validate(FixturesThemeFolder(), "valid_with_alpha_512x768.png");

        Assert.Null(result.Error);
        Assert.NotNull(result.Image);
    }

    [Fact]
    public void Validate_PngWithoutRealAlphaChannel_ReturnsNoAlphaChannel_DespiteValidPngFormat()
    {
        var validator = CreateValidator();

        var result = validator.Validate(FixturesThemeFolder(), "no_alpha_340x480.png");

        Assert.Equal(BackgroundImageValidationError.NoAlphaChannel, result.Error);
        Assert.Null(result.Image);
    }

    [Fact]
    public void Validate_DimensionsExceedCap_ReturnsDimensionsTooLarge()
    {
        // The fixture is 2000x2000; pin the caps below it so the dimension gate fires regardless
        // of the (larger) production default.
        var validator = new ThemeBackgroundImageValidator(
            ThemeBackgroundImageValidator.DefaultMaxFileSizeBytes, 1024, 1536);

        var result = validator.Validate(FixturesThemeFolder(), "oversized_2000x2000.png");

        Assert.Equal(BackgroundImageValidationError.DimensionsTooLarge, result.Error);
        Assert.Null(result.Image);
    }

    [Fact]
    public void Validate_FileSizeExceedsCap_ReturnsFileTooLarge_BeforeAnyDecodeAttempt()
    {
        // The fixture is ~2.5 MB; pin the cap below it so the size gate fires regardless of
        // the (larger) production default.
        var validator = new ThemeBackgroundImageValidator(2 * 1024 * 1024);

        var result = validator.Validate(FixturesThemeFolder(), "oversized_filesize.png");

        Assert.Equal(BackgroundImageValidationError.FileTooLarge, result.Error);
        Assert.Null(result.Image);
    }

    [Fact]
    public void Validate_TruncatedPng_ReturnsCorruptOrTruncated_NeverThrows()
    {
        var validator = CreateValidator();

        var result = validator.Validate(FixturesThemeFolder(), "truncated.png");

        Assert.Equal(BackgroundImageValidationError.CorruptOrTruncated, result.Error);
        Assert.Null(result.Image);
    }

    [Fact]
    public void Validate_NonPngContentNamedPng_ReturnsNotAPng_ExtensionIsNeverTrusted()
    {
        var validator = CreateValidator();

        var result = validator.Validate(FixturesThemeFolder(), "not_a_png.png");

        Assert.Equal(BackgroundImageValidationError.NotAPng, result.Error);
        Assert.Null(result.Image);
    }

    [Fact]
    public void Validate_MissingFile_ReturnsFileNotFound()
    {
        var validator = CreateValidator();

        var result = validator.Validate(FixturesThemeFolder(), "does_not_exist_anywhere.png");

        Assert.Equal(BackgroundImageValidationError.FileNotFound, result.Error);
        Assert.Null(result.Image);
    }

    [Theory]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    public void Validate_PathEscapesThemeFolder_ReturnsPathOutsideThemeFolder_NeverOpensFile(string maliciousPath)
    {
        var validator = CreateValidator();

        var result = validator.Validate(FixturesThemeFolder(), maliciousPath);

        Assert.Equal(BackgroundImageValidationError.PathOutsideThemeFolder, result.Error);
        Assert.Null(result.Image);
    }

    [Fact]
    public void Validate_SizeCheck_RunsBeforeDimensionOrAlphaChecks_PerDocumentedOrdering()
    {
        // oversized_filesize.png is also a valid, real-alpha PNG at 1024x1536 (within
        // the dimension cap) by construction — only its on-disk byte size exceeds the
        // pinned cap. If size were checked after decode, this would incorrectly pass or
        // fail for the wrong reason; asserting FileTooLarge here pins the §4.3 ordering.
        var validator = new ThemeBackgroundImageValidator(2 * 1024 * 1024);

        var result = validator.Validate(FixturesThemeFolder(), "oversized_filesize.png");

        Assert.Equal(BackgroundImageValidationError.FileTooLarge, result.Error);
    }
}
