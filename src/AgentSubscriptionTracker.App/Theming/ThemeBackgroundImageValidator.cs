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
    FileTooLarge,        // > 2 MB
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
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;
    private const int MaxWidth = 1024;
    private const int MaxHeight = 1536;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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

        if (fileInfo.Length > MaxFileSizeBytes)
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

            // BitmapCacheOption.OnLoad decodes metadata (dimensions/format) eagerly, but a
            // truncated pixel stream can still pass that and only fail once the actual pixel
            // data is read. Force a full pixel-buffer copy now so truncation/corruption in
            // the image data itself — not just the header — surfaces here as
            // CorruptOrTruncated, never as an uncaught exception later (e.g. from a
            // thumbnail renderer).
            var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            var buffer = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(buffer, stride, 0);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException or OverflowException or ArgumentException)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.CorruptOrTruncated);
        }

        if (frame.PixelWidth > MaxWidth || frame.PixelHeight > MaxHeight)
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.DimensionsTooLarge);
        }

        if (!HasRealAlphaChannel(frame.Format))
        {
            return new BackgroundImageValidationResult(null, BackgroundImageValidationError.NoAlphaChannel);
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
