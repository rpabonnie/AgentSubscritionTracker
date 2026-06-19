// SPEC-0004 §4 — strict, bounded deserialization target for theme.json. Immutable records only.

namespace AgentSubscriptionTracker.App.Theming;

/// <summary>Strict, bounded deserialization target for theme.json. Immutable.</summary>
public sealed record ThemeManifest
{
    public required int SchemaVersion { get; init; }
    public required string Name { get; init; }
    public required ThemeBackground Background { get; init; }
    public required ThemeFontSet Fonts { get; init; }
    public required ThemeBrushSet Brushes { get; init; }
    public required ThemeSeverityBands SeverityBands { get; init; }

    /// <summary>Current manifest schema version this build writes/expects. Loader accepts
    /// only this exact value for v1; unknown/future versions are treated as unsupported
    /// (skip-whole-file, never a partial/best-effort parse).</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary><see cref="ImagePath"/> is relative to the theme's own folder, or null
/// for "no image, fallback color only".</summary>
public sealed record ThemeBackground
{
    public string? ImagePath { get; init; }

    /// <summary>"#AARRGGBB" or "#RRGGBB" hex. Required even when ImagePath is set
    /// (used while the image loads and if it later degrades).</summary>
    public required string FallbackColor { get; init; }
}

public sealed record ThemeFontSet
{
    public required ThemeFont Header { get; init; }
    public required ThemeFont Body { get; init; }
    public required ThemeFont Footer { get; init; }
}

public sealed record ThemeFont
{
    public required string Family { get; init; }

    /// <summary>Points. Must be in (0, 96] after deserialization-time range validation.</summary>
    public required double Size { get; init; }

    /// <summary>"Normal" | "SemiBold" | "Bold" | "Light" (maps to FontWeights.*).</summary>
    public required string Weight { get; init; }
}

/// <summary>Exactly the 7 brush keys CalloutWindow.xaml already consumes via DynamicResource.
/// Each value is a "#AARRGGBB"/"#RRGGBB" hex string.</summary>
public sealed record ThemeBrushSet
{
    public required string CalloutBackgroundBrush { get; init; }
    public required string CalloutBorderBrush { get; init; }
    public required string TextPrimaryBrush { get; init; }
    public required string TextSecondaryBrush { get; init; }
    public required string BarTrackBrush { get; init; }
    public required string SeparatorBrush { get; init; }
    public required string AccentBrush { get; init; }
}

public sealed record ThemeSeverityBands
{
    public required string Ok { get; init; }
    public required string Warn { get; init; }
    public required string Critical { get; init; }
}
