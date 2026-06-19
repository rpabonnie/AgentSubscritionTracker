// SPEC-0004 §4/§4.1 — strict, bounded System.Text.Json parsing for theme.json. Never throws
// on malformed input; failures are reported via ThemeManifestParseResult.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentSubscriptionTracker.App.Theming;

/// <summary>Why ThemeManifestSerializer.TryParse failed. Never includes file content
/// or any path beyond the theme id (redaction-safe).</summary>
public enum ThemeManifestParseError
{
    InvalidJson,
    UnsupportedSchemaVersion,
    MissingRequiredField,
    OutOfRangeValue,
    InvalidColorFormat,
    PathTraversalRejected,
}

public readonly record struct ThemeManifestParseResult(
    ThemeManifest? Manifest,
    ThemeManifestParseError? Error);

/// <summary>Strict, bounded JSON parsing for theme.json. Never throws on malformed
/// input — failures are reported via <see cref="ThemeManifestParseResult"/>.</summary>
public interface IThemeManifestSerializer
{
    /// <summary>Parses theme.json content. Unknown JSON properties are ignored;
    /// every documented field is required (missing ⇒ MissingRequiredField) except
    /// <see cref="ThemeBackground.ImagePath"/> which may be null. Hex colors are
    /// validated for syntax (not yet resolved to a Brush — that is ThemeLoader's job).
    /// imagePath containing ".." segments, a rooted/absolute path, or a URI scheme
    /// (e.g. "file:", "http:") is rejected at parse time with PathTraversalRejected —
    /// it is never handed to the filesystem.</summary>
    ThemeManifestParseResult TryParse(string json);

    /// <summary>Serializes a manifest back to canonical JSON (used by the editor's Save
    /// path and by built-in re-seeding). Round-trips TryParse(Serialize(m)) == m.</summary>
    string Serialize(ThemeManifest manifest);
}

/// <summary>Strict, bounded JSON (de)serialization for theme.json (SPEC-0004 §4/§4.1).</summary>
public sealed class ThemeManifestSerializer : IThemeManifestSerializer
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc />
    public ThemeManifestParseResult TryParse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.InvalidJson);
        }

        using (document)
        {
            try
            {
                return ParseRoot(document.RootElement);
            }
            catch (JsonException)
            {
                return new ThemeManifestParseResult(null, ThemeManifestParseError.InvalidJson);
            }
        }
    }

    /// <inheritdoc />
    public string Serialize(ThemeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var dto = new ManifestDto
        {
            SchemaVersion = manifest.SchemaVersion,
            Name = manifest.Name,
            Background = new BackgroundDto
            {
                ImagePath = manifest.Background.ImagePath,
                FallbackColor = manifest.Background.FallbackColor,
            },
            Fonts = new FontSetDto
            {
                Header = ToFontDto(manifest.Fonts.Header),
                Body = ToFontDto(manifest.Fonts.Body),
                Footer = ToFontDto(manifest.Fonts.Footer),
            },
            Brushes = new BrushSetDto
            {
                CalloutBackgroundBrush = manifest.Brushes.CalloutBackgroundBrush,
                CalloutBorderBrush = manifest.Brushes.CalloutBorderBrush,
                TextPrimaryBrush = manifest.Brushes.TextPrimaryBrush,
                TextSecondaryBrush = manifest.Brushes.TextSecondaryBrush,
                BarTrackBrush = manifest.Brushes.BarTrackBrush,
                SeparatorBrush = manifest.Brushes.SeparatorBrush,
                AccentBrush = manifest.Brushes.AccentBrush,
            },
            SeverityBands = new SeverityBandsDto
            {
                Ok = manifest.SeverityBands.Ok,
                Warn = manifest.SeverityBands.Warn,
                Critical = manifest.SeverityBands.Critical,
            },
        };

        return JsonSerializer.Serialize(dto, SerializeOptions);
    }

    private static FontDto ToFontDto(ThemeFont font) =>
        new() { Family = font.Family, Size = font.Size, Weight = font.Weight };

    private static ThemeManifestParseResult ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.InvalidJson);
        }

        if (!TryGetRequiredInt(root, "schemaVersion", out var schemaVersion, out var missing))
        {
            return missing
                ? new ThemeManifestParseResult(null, ThemeManifestParseError.MissingRequiredField)
                : new ThemeManifestParseResult(null, ThemeManifestParseError.InvalidJson);
        }

        if (schemaVersion != ThemeManifest.CurrentSchemaVersion)
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.UnsupportedSchemaVersion);
        }

        if (!TryGetRequiredString(root, "name", out var name))
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.MissingRequiredField);
        }

        if (!root.TryGetProperty("background", out var backgroundElement)
            || backgroundElement.ValueKind != JsonValueKind.Object)
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.MissingRequiredField);
        }

        var backgroundResult = ParseBackground(backgroundElement);
        if (backgroundResult.Error is not null)
        {
            return new ThemeManifestParseResult(null, backgroundResult.Error);
        }

        if (!root.TryGetProperty("fonts", out var fontsElement) || fontsElement.ValueKind != JsonValueKind.Object)
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.MissingRequiredField);
        }

        var fontsResult = ParseFonts(fontsElement);
        if (fontsResult.Error is not null)
        {
            return new ThemeManifestParseResult(null, fontsResult.Error);
        }

        if (!root.TryGetProperty("brushes", out var brushesElement) || brushesElement.ValueKind != JsonValueKind.Object)
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.MissingRequiredField);
        }

        var brushesResult = ParseBrushes(brushesElement);
        if (brushesResult.Error is not null)
        {
            return new ThemeManifestParseResult(null, brushesResult.Error);
        }

        if (!root.TryGetProperty("severityBands", out var severityElement) || severityElement.ValueKind != JsonValueKind.Object)
        {
            return new ThemeManifestParseResult(null, ThemeManifestParseError.MissingRequiredField);
        }

        var severityResult = ParseSeverityBands(severityElement);
        if (severityResult.Error is not null)
        {
            return new ThemeManifestParseResult(null, severityResult.Error);
        }

        var manifest = new ThemeManifest
        {
            SchemaVersion = schemaVersion,
            Name = name,
            Background = backgroundResult.Background!,
            Fonts = fontsResult.Fonts!,
            Brushes = brushesResult.Brushes!,
            SeverityBands = severityResult.SeverityBands!,
        };

        return new ThemeManifestParseResult(manifest, null);
    }

    private static (ThemeBackground? Background, ThemeManifestParseError? Error) ParseBackground(JsonElement element)
    {
        if (!element.TryGetProperty("imagePath", out var imagePathElement))
        {
            return (null, ThemeManifestParseError.MissingRequiredField);
        }

        string? imagePath = null;
        if (imagePathElement.ValueKind == JsonValueKind.String)
        {
            imagePath = imagePathElement.GetString();
        }
        else if (imagePathElement.ValueKind != JsonValueKind.Null)
        {
            return (null, ThemeManifestParseError.InvalidJson);
        }

        if (!string.IsNullOrEmpty(imagePath) && !ThemePathResolver.IsSyntacticallySafe(imagePath))
        {
            return (null, ThemeManifestParseError.PathTraversalRejected);
        }

        if (!TryGetRequiredString(element, "fallbackColor", out var fallbackColor))
        {
            return (null, ThemeManifestParseError.MissingRequiredField);
        }

        if (!IsValidHexColor(fallbackColor))
        {
            return (null, ThemeManifestParseError.InvalidColorFormat);
        }

        return (new ThemeBackground { ImagePath = imagePath, FallbackColor = fallbackColor }, null);
    }

    private static (ThemeFontSet? Fonts, ThemeManifestParseError? Error) ParseFonts(JsonElement element)
    {
        var header = ParseFont(element, "header");
        if (header.Error is not null)
        {
            return (null, header.Error);
        }

        var body = ParseFont(element, "body");
        if (body.Error is not null)
        {
            return (null, body.Error);
        }

        var footer = ParseFont(element, "footer");
        if (footer.Error is not null)
        {
            return (null, footer.Error);
        }

        return (new ThemeFontSet { Header = header.Font!, Body = body.Font!, Footer = footer.Font! }, null);
    }

    private static (ThemeFont? Font, ThemeManifestParseError? Error) ParseFont(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var fontElement) || fontElement.ValueKind != JsonValueKind.Object)
        {
            return (null, ThemeManifestParseError.MissingRequiredField);
        }

        if (!TryGetRequiredString(fontElement, "family", out var family))
        {
            return (null, ThemeManifestParseError.MissingRequiredField);
        }

        if (!fontElement.TryGetProperty("size", out var sizeElement))
        {
            return (null, ThemeManifestParseError.MissingRequiredField);
        }

        double size;
        if (sizeElement.ValueKind == JsonValueKind.Number)
        {
            if (!sizeElement.TryGetDouble(out size))
            {
                return (null, ThemeManifestParseError.MissingRequiredField);
            }
        }
        else if (sizeElement.ValueKind == JsonValueKind.String
            && string.Equals(sizeElement.GetString(), "NaN", StringComparison.Ordinal))
        {
            // System.Text.Json's AllowNamedFloatingPointLiterals writes NaN as the JSON
            // string "NaN" (JSON has no native NaN/Infinity number token); accept that one
            // round-trip shape so OutOfRangeValue below — not a parse failure — is what
            // rejects it.
            size = double.NaN;
        }
        else
        {
            return (null, ThemeManifestParseError.MissingRequiredField);
        }

        if (double.IsNaN(size) || size <= 0 || size > 96)
        {
            return (null, ThemeManifestParseError.OutOfRangeValue);
        }

        if (!TryGetRequiredString(fontElement, "weight", out var weight))
        {
            return (null, ThemeManifestParseError.MissingRequiredField);
        }

        return (new ThemeFont { Family = family, Size = size, Weight = weight }, null);
    }

    private static (ThemeBrushSet? Brushes, ThemeManifestParseError? Error) ParseBrushes(JsonElement element)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] keys =
        [
            "CalloutBackgroundBrush", "CalloutBorderBrush", "TextPrimaryBrush", "TextSecondaryBrush",
            "BarTrackBrush", "SeparatorBrush", "AccentBrush",
        ];

        foreach (var key in keys)
        {
            if (!TryGetRequiredString(element, key, out var value))
            {
                return (null, ThemeManifestParseError.MissingRequiredField);
            }

            if (!IsValidHexColor(value))
            {
                return (null, ThemeManifestParseError.InvalidColorFormat);
            }

            values[key] = value;
        }

        return (new ThemeBrushSet
        {
            CalloutBackgroundBrush = values["CalloutBackgroundBrush"],
            CalloutBorderBrush = values["CalloutBorderBrush"],
            TextPrimaryBrush = values["TextPrimaryBrush"],
            TextSecondaryBrush = values["TextSecondaryBrush"],
            BarTrackBrush = values["BarTrackBrush"],
            SeparatorBrush = values["SeparatorBrush"],
            AccentBrush = values["AccentBrush"],
        }, null);
    }

    private static (ThemeSeverityBands? SeverityBands, ThemeManifestParseError? Error) ParseSeverityBands(JsonElement element)
    {
        if (!TryGetRequiredString(element, "ok", out var ok) || !IsValidHexColor(ok))
        {
            return (null, !TryGetRequiredString(element, "ok", out _)
                ? ThemeManifestParseError.MissingRequiredField
                : ThemeManifestParseError.InvalidColorFormat);
        }

        if (!TryGetRequiredString(element, "warn", out var warn) || !IsValidHexColor(warn))
        {
            return (null, !TryGetRequiredString(element, "warn", out _)
                ? ThemeManifestParseError.MissingRequiredField
                : ThemeManifestParseError.InvalidColorFormat);
        }

        if (!TryGetRequiredString(element, "critical", out var critical) || !IsValidHexColor(critical))
        {
            return (null, !TryGetRequiredString(element, "critical", out _)
                ? ThemeManifestParseError.MissingRequiredField
                : ThemeManifestParseError.InvalidColorFormat);
        }

        return (new ThemeSeverityBands { Ok = ok, Warn = warn, Critical = critical }, null);
    }

    private static bool TryGetRequiredInt(JsonElement element, string propertyName, out int value, out bool missing)
    {
        value = 0;
        missing = false;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            missing = true;
            return false;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out value))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetRequiredString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var str = property.GetString();
        if (string.IsNullOrEmpty(str))
        {
            return false;
        }

        value = str;
        return true;
    }

    /// <summary>Accepts "#RRGGBB" (alpha defaults to FF) or "#AARRGGBB" hex syntax only.</summary>
    private static bool IsValidHexColor(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '#')
        {
            return false;
        }

        var hex = value.AsSpan(1);
        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        foreach (var c in hex)
        {
            var isHexDigit = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }

    // DTO shapes mirror the public records but use init-only mutable properties for
    // System.Text.Json source-generation-free (reflection) serialization with camelCase names.
    private sealed class ManifestDto
    {
        public int SchemaVersion { get; set; }
        public string Name { get; set; } = string.Empty;
        public BackgroundDto Background { get; set; } = new();
        public FontSetDto Fonts { get; set; } = new();
        public BrushSetDto Brushes { get; set; } = new();
        public SeverityBandsDto SeverityBands { get; set; } = new();
    }

    private sealed class BackgroundDto
    {
        public string? ImagePath { get; set; }
        public string FallbackColor { get; set; } = string.Empty;
    }

    private sealed class FontSetDto
    {
        public FontDto Header { get; set; } = new();
        public FontDto Body { get; set; } = new();
        public FontDto Footer { get; set; } = new();
    }

    private sealed class FontDto
    {
        public string Family { get; set; } = string.Empty;

        // Out-of-range tests (incl. NaN) round-trip a manifest through Serialize() before
        // re-parsing it to confirm TryParse rejects the value; AllowNamedFloatingPointLiterals
        // lets System.Text.Json write/read "NaN" so that probe doesn't blow up before TryParse
        // ever gets a chance to apply the §4.1 OutOfRangeValue rule.
        [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
        public double Size { get; set; }

        public string Weight { get; set; } = string.Empty;
    }

    private sealed class BrushSetDto
    {
        [JsonPropertyName("CalloutBackgroundBrush")]
        public string CalloutBackgroundBrush { get; set; } = string.Empty;

        [JsonPropertyName("CalloutBorderBrush")]
        public string CalloutBorderBrush { get; set; } = string.Empty;

        [JsonPropertyName("TextPrimaryBrush")]
        public string TextPrimaryBrush { get; set; } = string.Empty;

        [JsonPropertyName("TextSecondaryBrush")]
        public string TextSecondaryBrush { get; set; } = string.Empty;

        [JsonPropertyName("BarTrackBrush")]
        public string BarTrackBrush { get; set; } = string.Empty;

        [JsonPropertyName("SeparatorBrush")]
        public string SeparatorBrush { get; set; } = string.Empty;

        [JsonPropertyName("AccentBrush")]
        public string AccentBrush { get; set; } = string.Empty;
    }

    private sealed class SeverityBandsDto
    {
        public string Ok { get; set; } = string.Empty;
        public string Warn { get; set; } = string.Empty;
        public string Critical { get; set; } = string.Empty;
    }
}
