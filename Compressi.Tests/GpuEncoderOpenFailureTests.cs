using Compressi.Core.Services;

namespace Compressi.Tests;

public class GpuEncoderOpenFailureTests
{
    [Fact]
    public void IsGpuEncoderOpenFailure_NvencApiMismatch_ReturnsTrue()
    {
        const string error = """
            [av1_nvenc @ 00000252b4a18480] Driver does not support the required nvenc API version. Required: 13.1 Found: 13.0
            [av1_nvenc @ 00000252b4a18480] The minimum required Nvidia driver for nvenc is 610.00 or newer
            [vost#0:0/av1_nvenc @ 00000252b65add80] [enc:av1_nvenc @ 00000252b49bce80] Error while opening encoder - maybe incorrect parameters such as bit_rate, rate, width or height.
            Error opening output file C:\out.mp4.
            Error opening output files: Function not implemented
            Conversion failed!
            """;

        Assert.True(GpuEncoderOpenFailure.IsGpuEncoderOpenFailure(error));
    }

    [Fact]
    public void IsGpuEncoderOpenFailure_GenericInputError_ReturnsFalse()
    {
        const string error = """
            [in#0 @ 000001] Error opening input: No such file or directory
            Error opening input file missing.mp4.
            Error opening input files: No such file or directory
            """;

        Assert.False(GpuEncoderOpenFailure.IsGpuEncoderOpenFailure(error));
    }

    [Fact]
    public void IsGpuEncoderOpenFailure_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(GpuEncoderOpenFailure.IsGpuEncoderOpenFailure(null));
        Assert.False(GpuEncoderOpenFailure.IsGpuEncoderOpenFailure(string.Empty));
        Assert.False(GpuEncoderOpenFailure.IsGpuEncoderOpenFailure("   "));
    }

    [Theory]
    [InlineData("av1_qsv: Error while opening encoder")]
    [InlineData("av1_amf Device creation failed")]
    [InlineData("No NVENC capable devices found")]
    public void IsGpuEncoderOpenFailure_OtherGpuOpenErrors_ReturnsTrue(string error)
    {
        Assert.True(GpuEncoderOpenFailure.IsGpuEncoderOpenFailure(error));
    }
}
