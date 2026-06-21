// SPEC-0004 §4.3 — background image validation pipeline: containment, existence, size,
// decode, dimensions, alpha — in that order, short-circuiting on the first failure.

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgentSubscriptionTracker.App.Theming;

/// <summary>Resolves and opens a theme's background image safely. Never throws;
/// failures are reported via <see cref="BackgroundImageValidationResult"/>.</summary>
public enum BackgroundImageValidationError
{
    PathOutsideThemeFolder,
    FileNotFound,
    FileTooLarge,        // over the configured cap (default 30 MB)
    NotAPng,
    CorruptOrTruncated,
    DimensionsTooLarge,  // > 1024x1536 decoded
    NoAlphaChannel,
}

public readonly record struct BackgroundImageValidationResult(
    BitmapSource? Image,
    BackgroundImageValidationError? Error);

public interface IThemeBackgroundImageValidator
{
    /// <summary>
    /// <paramref name="themeFolder"/> is the theme's own absolute folder;
    /// <paramref name="imagePath"/> is the manifest's (already syntax-validated,
    /// non-traversal) relative path. Resolves+canonicalizes
    /// <paramref name="imagePath"/> against <paramref name="themeFolder"/>, verifies
    /// containment (defense-in-depth — duplicates the serializer's syntax check
    /// against a real filesystem path), checks file size, decodes, checks
    /// PixelFormat for a real alpha channel, checks decoded pixel dimensions.
    /// Never opens a file whose canonicalized path escapes <paramref name="themeFolder"/>.
    /// </summary>
    BackgroundImageValidationResult Validate(string themeFolder, string imagePath);
}

/// <summary>PNG-only background image validation pipeline (SPEC-0004 §4.3).</summary>
public sealed class ThemeBackgroundImageValidator : IThemeBackgroundImageValidator
{
    /// <summary>Default background-image file-size cap (30 MB). Checked on
    /// <see cref="FileInfo.Length"/> before any byte of the image is read/decoded.</summary>
    public const long DefaultMaxFileSizeBytes = 30L * 1024 * 1024;

    /// <summary>Default decoded pixel-dimension caps. Bounds decode memory (an 8192×8192 RGBA
    /// image is ~256 MB) and is checked from the PNG header BEFORE the full-resolution decode,
    /// so an oversized image degrades to the fallback color instead of OOM-crashing the decoder.</summary>
    public const int DefaultMaxWidth = 8192;
    public const int DefaultMaxHeight = 8192;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly long _maxFileSizeBytes;
    private readonly int _maxWidth;
    private readonly int _maxHeight;

    /// <summary>Uses the default 30 MB file-size cap and 8192×8192 dimension caps.</summary>
    public ThemeBackgroundImageValidator()
        : this(DefaultMaxFileSizeBytes, DefaultMaxWidth, DefaultMaxHeight)
    {
    }

    /// <summary>Overrides the file-size cap, keeping the default dimension caps.</summary>
    public ThemeBackgroundImageValidator(long maxFileSizeBytes)
        : this(maxFileSizeBytes, DefaultMaxWidth, DefaultMaxHeight)
    {
    }

    /// <summary>Overrides every cap (e.g. for tests that pin a specific gate with a small
    /// fixture).</summary>
    public ThemeBackgroundImageValidator(long maxFileSizeBytes, int maxWidth, int maxHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;
    }

    // "IEND" — the PNG end-of-stream marker chunk type. WPF's PngBitmapDecoder is lenient
    // about a truncated IDAT stream (it silently zero-fills missing scanlines rather than
    // throwing), so a structural "does the file actually end with a complete IEND chunk"
    // check is the reliable signal for "truncated mid-stream" rather than the decoder's
    // own (best-effort) exception behavior.
    private static readonly byte[] IendChunkType = [0x49, 0x45, 0x4E, 0x44];

    /// <inheritdoc />
    public BackgroundImageValidationResult Validate(string themeFolder, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(themeFolder);
        ArgumentNullException.ThrowIfNull(imagePath);

        if (!ThemePathResolver.TryResolveContained(themeFolder, imagePath, out var canonicalPath))
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.PathOutsideThemeFolder);
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(canonicalPath);
            if (!fileInfo.Exists)
            {
                return new BackgroundImageValidationResult(null, BackgroundImageValidationError.FileNotFound);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or UnauthorizedAccessException)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.FileNotFound);
        }

        if (fileInfo.Length > _maxFileSizeBytes)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.FileTooLarge);
        }

        byte[] header;
        try
        {
            using var stream = File.OpenRead(canonicalPath);
            header = new byte[8];
            var read = stream.Read(header, 0, header.Length);
            if (read < header.Length || !header.AsSpan().SequenceEqual(PngSignature))
            {
                return new BackgroundImageValidationResult(null, BackgroundImageValidationError.NotAPng);
            }
        }
        catch (IOException)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.CorruptOrTruncated);
        }
        catch (UnauthorizedAccessException)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.FileNotFound);
        }

        if (!EndsWithCompleteIendChunk(canonicalPath))
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.CorruptOrTruncated);
        }

        // Read dimensions + pixel format from the PNG header WITHOUT decoding pixels
        // (DelayCreation defers pixel decode; those values come from the IHDR chunk). This lets
        // us reject an oversized image BEFORE the full-resolution decode below, so a huge image
        // degrades gracefully instead of OOM-crashing the eager OnLoad decoder.
        int pixelWidth;
        int pixelHeight;
        PixelFormat pixelFormat;
        try
        {
            using var headerStream = File.OpenRead(canonicalPath);
            var headerDecoder = new PngBitmapDecoder(headerStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (headerDecoder.Frames.Count == 0)
            {
                return new BackgroundImageValidationResult(null, BackgroundImageValidationError.CorruptOrTruncated);
            }

            var headerFrame = headerDecoder.Frames[0];
            pixelWidth = headerFrame.PixelWidth;
            pixelHeight = headerFrame.PixelHeight;
            pixelFormat = headerFrame.Format;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException or OverflowException or ArgumentException)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.CorruptOrTruncated);
        }

        if (pixelWidth > _maxWidth || pixelHeight > _maxHeight)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.DimensionsTooLarge);
        }

        if (!HasRealAlphaChannel(pixelFormat))
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.NoAlphaChannel);
        }

        // Dimensions are now bounded by the cap above, so this full decode + pixel-buffer copy
        // is memory-bounded. The copy forces truncation/corruption in the image data itself —
        // not just the header — to surface as CorruptOrTruncated rather than an uncaught
        // exception later (e.g. from a thumbnail renderer).
        BitmapFrame frame;
        try
        {
            using var stream = File.OpenRead(canonicalPath);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                return new BackgroundImageValidationResult(null, BackgroundImageValidationError.CorruptOrTruncated);
            }

            frame = decoder.Frames[0];

            var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            var buffer = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(buffer, stride, 0);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException or OverflowException or ArgumentException)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.CorruptOrTruncated);
        }

        return new BackgroundImageValidationResult(frame, null);
    }

    private static bool HasRealAlphaChannel(PixelFormat format) =>
        format == PixelFormats.Bgra32
        || format == PixelFormats.Pbgra32
        || format == PixelFormats.Rgba64
        || format == PixelFormats.Prgba64
        || format == PixelFormats.Rgba128Float
        || format == PixelFormats.Prgba128Float;

    /// <summary>True when the file's last 8 bytes are the standard zero-length IEND chunk
    /// (length 0x00000000 + "IEND" + its CRC) — the canonical PNG end-of-stream marker.
    /// A file truncated mid-IDAT will not end this way even though WPF's lenient decoder
    /// may still "succeed" by zero-filling missing scanlines.</summary>
    private static bool EndsWithCompleteIendChunk(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 8)
            {
                return false;
            }

            var tail = new byte[8];
            stream.Seek(-8, SeekOrigin.End);
            var read = stream.Read(tail, 0, tail.Length);
            if (read < tail.Length)
            {
                return false;
            }

            // The IEND chunk has zero-length data, so its on-disk layout is
            // [4-byte length=0][4-byte type "IEND"][0 bytes of data][4-byte CRC] = 12 bytes
            // total, but length+type (8 bytes) precede the file's final 4-byte CRC. The
            // *last* 8 bytes of a well-formed PNG are therefore [type "IEND"][CRC].
            return tail.AsSpan(0, 4).SequenceEqual(IendChunkType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
