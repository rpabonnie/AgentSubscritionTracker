// SPEC-0004 §4.2/§7 — pure path-containment helper for imagePath resolution.
// Spec-phase stub: references AgentSubscriptionTracker.App.Theming types that do not
// exist yet, so the test project intentionally does not compile until TASK-016
// implements SPEC-0004.

using AgentSubscriptionTracker.App.Theming;

namespace AgentSubscriptionTracker.Tests.Theming;

public sealed class ThemePathResolverTests
{
    private static readonly string ThemeFolder = Path.Combine(Path.GetTempPath(), "AST-Theme-Test-Folder");

    [Fact]
    public void TryResolveContained_PlainRelativePath_ResolvesAndIsContained()
    {
        var ok = ThemePathResolver.TryResolveContained(ThemeFolder, "background.png", out var resolved);

        Assert.True(ok);
        Assert.StartsWith(Path.GetFullPath(ThemeFolder), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolveContained_RelativePathInSubfolder_ResolvesAndIsContained()
    {
        var ok = ThemePathResolver.TryResolveContained(ThemeFolder, "assets\\background.png", out var resolved);

        Assert.True(ok);
        Assert.StartsWith(Path.GetFullPath(ThemeFolder), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("..\\background.png")]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("assets\\..\\..\\escape.png")]
    public void TryResolveContained_DotDotEscape_IsRejected(string traversalPath)
    {
        var ok = ThemePathResolver.TryResolveContained(ThemeFolder, traversalPath, out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("D:\\anywhere.png")]
    public void TryResolveContained_AbsoluteWindowsPath_IsRejected(string absolutePath)
    {
        var ok = ThemePathResolver.TryResolveContained(ThemeFolder, absolutePath, out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("\\\\host\\share\\background.png")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("http://example.com/background.png")]
    [InlineData("https://example.com/background.png")]
    public void TryResolveContained_UncOrUriLikePath_IsRejected(string uriLikePath)
    {
        var ok = ThemePathResolver.TryResolveContained(ThemeFolder, uriLikePath, out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryResolveContained_NullOrEmptyPath_IsRejected(string? emptyPath)
    {
        var ok = ThemePathResolver.TryResolveContained(ThemeFolder, emptyPath!, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryResolveContained_CaseAndSeparatorVariants_StillResolveCorrectly()
    {
        var ok = ThemePathResolver.TryResolveContained(ThemeFolder, "Background.PNG", out var resolved);

        Assert.True(ok);
        Assert.StartsWith(Path.GetFullPath(ThemeFolder), resolved, StringComparison.OrdinalIgnoreCase);
    }
}
