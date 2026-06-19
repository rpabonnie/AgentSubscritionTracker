// SPEC-0004 §4 — filesystem persistence for themes: Save-As/fork, in-place overwrite
// (non-built-ins only), and delete. Not unit-tested directly (no SPEC-0004 test stub
// references it); exercised via the theme editor/picker shell, verified at TASK-019.

using System.IO;
using System.Text;

namespace AgentSubscriptionTracker.App.Theming;

public interface IThemeStore
{
    /// <summary>True when <paramref name="themeId"/> is one of the 2 read-only built-ins.</summary>
    bool IsBuiltIn(string themeId);

    /// <summary>Persists <paramref name="manifest"/> (+ optional background image bytes)
    /// under a new theme id derived from <paramref name="manifest"/>.Name, guaranteed
    /// unique even when the name duplicates an existing theme's display name (ids never
    /// collide; duplicate display names are allowed and disambiguated for display only,
    /// per §4.4). Always used instead of direct overwrite when the source is a built-in.</summary>
    string SaveAsNew(ThemeManifest manifest, byte[]? backgroundPngBytes);

    /// <summary>Overwrites an existing non-built-in theme in place. Throws
    /// InvalidOperationException if <paramref name="themeId"/> IsBuiltIn — callers
    /// must route built-ins through SaveAsNew.</summary>
    void Overwrite(string themeId, ThemeManifest manifest, byte[]? backgroundPngBytes);

    /// <summary>Deletes a non-built-in theme folder. Throws InvalidOperationException
    /// for a built-in id.</summary>
    void Delete(string themeId);
}

/// <summary>Filesystem-backed theme persistence under the themes root (SPEC-0004 §4).</summary>
public sealed class ThemeStore : IThemeStore
{
    private readonly string _themesRoot;
    private readonly IThemeManifestSerializer _serializer;

    public ThemeStore(string themesRoot)
        : this(themesRoot, new ThemeManifestSerializer())
    {
    }

    public ThemeStore(string themesRoot, IThemeManifestSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(themesRoot);
        ArgumentNullException.ThrowIfNull(serializer);
        _themesRoot = themesRoot;
        _serializer = serializer;
    }

    /// <inheritdoc />
    public bool IsBuiltIn(string themeId) =>
        themeId is ThemeLoader.BuiltInDarkThemeId or ThemeLoader.BuiltInLightThemeId;

    /// <inheritdoc />
    public string SaveAsNew(ThemeManifest manifest, byte[]? backgroundPngBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var themeId = GenerateUniqueThemeId(manifest.Name);
        WriteThemeFolder(themeId, manifest, backgroundPngBytes);
        return themeId;
    }

    /// <inheritdoc />
    public void Overwrite(string themeId, ThemeManifest manifest, byte[]? backgroundPngBytes)
    {
        ArgumentNullException.ThrowIfNull(themeId);
        ArgumentNullException.ThrowIfNull(manifest);

        if (IsBuiltIn(themeId))
        {
            throw new InvalidOperationException(
                "Built-in themes are read-only; route the edit through SaveAsNew to fork it.");
        }

        WriteThemeFolder(themeId, manifest, backgroundPngBytes);
    }

    /// <inheritdoc />
    public void Delete(string themeId)
    {
        ArgumentNullException.ThrowIfNull(themeId);

        if (IsBuiltIn(themeId))
        {
            throw new InvalidOperationException("Built-in themes cannot be deleted.");
        }

        var folder = Path.Combine(_themesRoot, themeId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private void WriteThemeFolder(string themeId, ThemeManifest manifest, byte[]? backgroundPngBytes)
    {
        var folder = Path.Combine(_themesRoot, themeId);
        Directory.CreateDirectory(folder);

        var json = _serializer.Serialize(manifest);
        File.WriteAllText(Path.Combine(folder, "theme.json"), json, Encoding.UTF8);

        if (backgroundPngBytes is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, "background.png"), backgroundPngBytes);
        }
    }

    private string GenerateUniqueThemeId(string displayName)
    {
        var baseSlug = Slugify(displayName);
        if (!ThemeIdExists(baseSlug))
        {
            return baseSlug;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = baseSlug + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ThemeIdExists(candidate))
            {
                return candidate;
            }
        }

        // Unreachable in practice; satisfies the compiler's exhaustiveness expectation.
        return baseSlug + "-" + Guid.NewGuid().ToString("N");
    }

    private bool ThemeIdExists(string themeId) => Directory.Exists(Path.Combine(_themesRoot, themeId));

    private static string Slugify(string displayName)
    {
        var builder = new StringBuilder(displayName.Length);
        foreach (var c in displayName)
        {
            if (char.IsLetterOrDigit(c))
            {
#pragma warning disable CA1308
                builder.Append(char.ToLowerInvariant(c));
#pragma warning restore CA1308
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length > 0 ? slug : "theme";
    }
}
