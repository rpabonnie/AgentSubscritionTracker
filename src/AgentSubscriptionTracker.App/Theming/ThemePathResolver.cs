// SPEC-0004 §4.2 — pure path-containment helper for imagePath resolution. No I/O beyond
// Path.GetFullPath's string math; never opens a file itself.

using System.IO;
using System.Linq;

namespace AgentSubscriptionTracker.App.Theming;

/// <summary>Pure path-containment helper (no I/O beyond Path.GetFullPath's string math).</summary>
public static class ThemePathResolver
{
    /// <summary>True and outputs the canonicalized absolute path only when
    /// <paramref name="relativeImagePath"/>, resolved against
    /// <paramref name="themeFolder"/> and canonicalized, is still a descendant of
    /// <paramref name="themeFolder"/>. Rejects absolute/rooted paths, ".." escapes,
    /// and any path containing a URI scheme (":" other than a Windows drive letter
    /// at position 1) outright — they never reach Path.GetFullPath's resolution
    /// against the theme folder.</summary>
    public static bool TryResolveContained(
        string themeFolder, string relativeImagePath, out string canonicalAbsolutePath)
    {
        canonicalAbsolutePath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativeImagePath))
        {
            return false;
        }

        if (HasDisallowedSyntax(relativeImagePath))
        {
            return false;
        }

        string canonicalThemeFolder;
        string canonicalCandidate;
        try
        {
            canonicalThemeFolder = Path.GetFullPath(themeFolder);
            canonicalCandidate = Path.GetFullPath(Path.Combine(canonicalThemeFolder, relativeImagePath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        var themeFolderWithSeparator = canonicalThemeFolder.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!canonicalCandidate.StartsWith(themeFolderWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        canonicalAbsolutePath = canonicalCandidate;
        return true;
    }

    /// <summary>Syntax-only check (no filesystem access) used by
    /// <see cref="ThemeManifestSerializer"/> at parse time, before a theme folder is even
    /// known. True when <paramref name="relativeImagePath"/> contains no traversal/absolute/
    /// URI-scheme syntax; false otherwise (including null/empty).</summary>
    public static bool IsSyntacticallySafe(string? relativeImagePath) =>
        !string.IsNullOrWhiteSpace(relativeImagePath) && !HasDisallowedSyntax(relativeImagePath);

    private static bool HasDisallowedSyntax(string relativeImagePath)
    {
        if (Path.IsPathRooted(relativeImagePath))
        {
            return true;
        }

        // UNC paths ("\\host\share\...").
        if (relativeImagePath.StartsWith(@"\\", StringComparison.Ordinal)
            || relativeImagePath.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        // ".." path-segment escape, tolerant of either separator.
        var segments = relativeImagePath.Split('\\', '/');
        if (segments.Any(s => s == ".."))
        {
            return true;
        }

        // URI scheme detection: a ':' that is not the second character of a
        // "C:" style drive-letter prefix (which IsPathRooted already catches, but
        // we double-check here defensively) is treated as a scheme like "file:" or
        // "http:" and rejected outright.
        var colonIndex = relativeImagePath.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex >= 0)
        {
            var isDriveLetterPattern = colonIndex == 1 && char.IsLetter(relativeImagePath[0]);
            if (!isDriveLetterPattern)
            {
                return true;
            }

            // Even a drive-letter-shaped prefix elsewhere in a "relative" path is rooted
            // syntax that should never be combined with the theme folder.
            return true;
        }

        return false;
    }
}
