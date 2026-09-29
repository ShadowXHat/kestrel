using Kestrel.Core.Ingest;
using Xunit;

namespace Kestrel.Tests.Ingest;

public class EvtxFileValidatorTests
{
    private const long MaxBytes = 1024 * 1024;

    [Fact]
    public void Valid_evtx_magic_is_accepted()
    {
        var result = EvtxFileValidator.ValidateHeader("ElfFile\0"u8, declaredSize: 4096, maxFileBytes: MaxBytes);

        Assert.True(result.IsValid, result.Reason);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Wrong_magic_is_rejected_with_reason()
    {
        var result = EvtxFileValidator.ValidateHeader("NotEvtx!"u8, declaredSize: null, maxFileBytes: MaxBytes);

        Assert.False(result.IsValid);
        Assert.Contains("magic", result.Reason);
    }

    [Fact]
    public void Short_header_is_rejected()
    {
        var result = EvtxFileValidator.ValidateHeader("ElfFil"u8, declaredSize: null, maxFileBytes: MaxBytes);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Declared_oversize_is_rejected()
    {
        var result = EvtxFileValidator.ValidateHeader("ElfFile\0"u8, declaredSize: MaxBytes + 1, maxFileBytes: MaxBytes);

        Assert.False(result.IsValid);
        Assert.Contains("maximum", result.Reason);
    }

    [Fact]
    public void Declared_tiny_size_is_rejected()
    {
        var result = EvtxFileValidator.ValidateHeader("ElfFile\0"u8, declaredSize: 4, maxFileBytes: MaxBytes);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Magic_prefix_is_case_sensitive_per_spec()
    {
        Assert.True(EvtxFileValidator.StartsWithMagic("ElfFile\0"u8));
        Assert.False(EvtxFileValidator.StartsWithMagic("elffile\0"u8));
        Assert.Equal(8, EvtxFileValidator.MagicLength);
    }
}