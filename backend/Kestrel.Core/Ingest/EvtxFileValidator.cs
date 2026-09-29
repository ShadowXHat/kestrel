namespace Kestrel.Core.Ingest;

/// <summary>
/// Magic-byte validation for EVTX uploads (.clinerules security rule). A
/// Windows EVTX file starts with the eight ASCII bytes <c>ElfFile\0</c>.
/// Validation is cheap and synchronous: it runs before any disk work is
/// committed, so junk uploads cost (almost) nothing.
/// </summary>
public static class EvtxFileValidator
{
    public const int MagicLength = 8;

    private static readonly byte[] Magic = "ElfFile\0"u8.ToArray();

    /// <summary>The EVTX magic prefix.</summary>
    public static ReadOnlySpan<byte> MagicBytes => Magic;

    /// <summary>True when the bytes start with the EVTX magic prefix.</summary>
    public static bool StartsWithMagic(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Magic);

    /// <param name="header">The leading bytes read from the stream (may be short on a truncated body).</param>
    /// <param name="declaredSize">Content-Length when known; chunked uploads pass null.</param>
    /// <param name="maxFileBytes">Configured maximum upload size.</param>
    public static EvtxValidationResult ValidateHeader(ReadOnlySpan<byte> header, long? declaredSize, long maxFileBytes)
    {
        if (declaredSize is { } size)
        {
            if (size < MagicLength)
            {
                return EvtxValidationResult.Invalid(
                    $"Body declares {size} bytes, smaller than the EVTX magic prefix ({MagicLength} bytes).");
            }

            if (size > maxFileBytes)
            {
                return EvtxValidationResult.Invalid(
                    $"Body declares {size} bytes, exceeding the configured maximum of {maxFileBytes} bytes " +
                    "(KestrelApp:Ingest:MaxUploadBytes).");
            }
        }

        if (header.Length < MagicLength)
        {
            return EvtxValidationResult.Invalid(
                $"Body is smaller than the EVTX magic prefix ({MagicLength} bytes) — not an EVTX file.");
        }

        if (!StartsWithMagic(header))
        {
            return EvtxValidationResult.Invalid(
                "Content does not start with the EVTX magic bytes (\"ElfFile\\0\") — not an EVTX file.");
        }

        return EvtxValidationResult.Ok();
    }
}

/// <summary>Result of <see cref="EvtxFileValidator.ValidateHeader"/>.</summary>
public readonly record struct EvtxValidationResult(bool IsValid, string? Reason)
{
    public static EvtxValidationResult Ok() => new(true, null);

    public static EvtxValidationResult Invalid(string reason) => new(false, reason);
}