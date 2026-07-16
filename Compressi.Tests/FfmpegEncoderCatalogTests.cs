using Compressi.Core.Services;

namespace Compressi.Tests;

public class FfmpegEncoderCatalogTests : IDisposable
{
    public FfmpegEncoderCatalogTests()
    {
        FfmpegEncoderCatalog.ResetForTests();
    }

    public void Dispose()
    {
        FfmpegEncoderCatalog.ResetForTests();
    }

    [Fact]
    public void GetPreferredGpuEncoder_SkipsListedButUnusableNvenc()
    {
        FfmpegEncoderCatalog.SetEncoderListOverride("""
            V..... libsvtav1
            V..... av1_nvenc
            V..... av1_qsv
            """);
        FfmpegEncoderCatalog.SetGpuProbeOverride((_, encoder) => encoder == "av1_qsv");

        FfmpegEncoderCatalog.Refresh();

        Assert.Equal("av1_qsv", FfmpegEncoderCatalog.GetPreferredGpuEncoder());
        Assert.Equal(new[] { "av1_qsv" }, FfmpegEncoderCatalog.GetAvailableGpuEncoders());
    }

    [Fact]
    public void GetPreferredGpuEncoder_ReturnsNullWhenAllGpuProbesFail()
    {
        FfmpegEncoderCatalog.SetEncoderListOverride("""
            V..... libsvtav1
            V..... av1_nvenc
            """);
        FfmpegEncoderCatalog.SetGpuProbeOverride((_, _) => false);

        FfmpegEncoderCatalog.Refresh();

        Assert.Null(FfmpegEncoderCatalog.GetPreferredGpuEncoder());
        Assert.Empty(FfmpegEncoderCatalog.GetAvailableGpuEncoders());
        Assert.Equal("libsvtav1", FfmpegEncoderCatalog.GetCpuAv1Encoder());
    }

    [Fact]
    public void GetPreferredGpuEncoder_StopsProbingAfterFirstSuccess()
    {
        var probed = new List<string>();
        FfmpegEncoderCatalog.SetEncoderListOverride("""
            V..... libsvtav1
            V..... av1_nvenc
            V..... av1_qsv
            V..... av1_amf
            """);
        FfmpegEncoderCatalog.SetGpuProbeOverride((_, encoder) =>
        {
            probed.Add(encoder);
            return encoder == "av1_nvenc";
        });

        FfmpegEncoderCatalog.Refresh();

        Assert.Equal("av1_nvenc", FfmpegEncoderCatalog.GetPreferredGpuEncoder());
        Assert.Equal(new[] { "av1_nvenc" }, probed);
        Assert.Equal(new[] { "av1_nvenc" }, FfmpegEncoderCatalog.GetAvailableGpuEncoders());
    }
}
