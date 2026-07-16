namespace Compressi.Core.Services;

public static class GpuEncoderOpenFailure
{
    private static readonly string[] GpuEncoderTokens = ["av1_nvenc", "av1_qsv", "av1_amf", "nvenc", "qsv", "amf"];

    private static readonly string[] OpenFailureMarkers =
    [
        "Error while opening encoder",
        "Driver does not support the required nvenc API",
        "does not support the required nvenc API version",
        "The minimum required Nvidia driver for nvenc",
        "Cannot load nvcuda.dll",
        "No NVENC capable devices found",
        "OpenEncodeSessionEx failed",
        "Failed to create a session",
        "Device creation failed",
        "Failed loading driver",
    ];

    public static bool IsGpuEncoderOpenFailure(string? errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText))
        {
            return false;
        }

        if (!ContainsAny(errorText, GpuEncoderTokens))
        {
            return false;
        }

        if (ContainsAny(errorText, OpenFailureMarkers))
        {
            return true;
        }

        // FFmpeg often follows a failed GPU open with ENOSYS / EINVAL noise.
        return errorText.Contains("Function not implemented", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("Invalid argument", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string haystack, string[] needles)
    {
        for (var i = 0; i < needles.Length; i++)
        {
            if (haystack.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
