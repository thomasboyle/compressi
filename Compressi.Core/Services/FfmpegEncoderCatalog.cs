using System.Diagnostics;
using System.Text;

namespace Compressi.Core.Services;

public static class FfmpegEncoderCatalog
{
    private static readonly string[] GpuEncoderPriority = ["av1_nvenc", "av1_qsv", "av1_amf"];
    private const int GpuProbeTimeoutMs = 8_000;

    private static readonly object Gate = new();
    private static bool _initialized;
    private static string _cpuAv1Encoder = "libsvtav1";
    private static string? _preferredGpuEncoder;
    private static IReadOnlyList<string> _availableGpuEncoders = Array.Empty<string>();
    private static Func<string, string, bool>? _gpuProbeOverride;
    private static string? _encoderListOverride;

    public static string GetCpuAv1Encoder()
    {
        EnsureInitialized();
        return _cpuAv1Encoder;
    }

    public static string? GetPreferredGpuEncoder()
    {
        EnsureInitialized();
        return _preferredGpuEncoder;
    }

    public static IReadOnlyList<string> GetAvailableGpuEncoders()
    {
        EnsureInitialized();
        return _availableGpuEncoders;
    }

    public static void Refresh()
    {
        lock (Gate)
        {
            _initialized = false;
            EnsureInitialized();
        }
    }

    /// <summary>
    /// Test seam: when set, replaces the real ffmpeg GPU open-probe.
    /// Pass null to restore the default probe.
    /// </summary>
    internal static void SetGpuProbeOverride(Func<string, string, bool>? probe)
    {
        lock (Gate)
        {
            _gpuProbeOverride = probe;
            _initialized = false;
        }
    }

    /// <summary>
    /// Test seam: when set, replaces the ffmpeg -encoders listing.
    /// Pass null to restore the default query.
    /// </summary>
    internal static void SetEncoderListOverride(string? encoderList)
    {
        lock (Gate)
        {
            _encoderListOverride = encoderList;
            _initialized = false;
        }
    }

    /// <summary>
    /// Test seam: clears overrides and forces re-initialization on next access.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _gpuProbeOverride = null;
            _encoderListOverride = null;
            _initialized = false;
            _cpuAv1Encoder = "libsvtav1";
            _preferredGpuEncoder = null;
            _availableGpuEncoders = Array.Empty<string>();
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            var encoderList = QueryEncoders();
            _cpuAv1Encoder = encoderList.Contains("libsvtav1", StringComparison.Ordinal)
                ? "libsvtav1"
                : encoderList.Contains("libaom-av1", StringComparison.Ordinal)
                    ? "libaom-av1"
                    : "libsvtav1";

            var ffmpegPath = FfmpegToolPaths.GetFfmpegPath();
            var ffmpegExists = File.Exists(ffmpegPath);
            // Preferred is priority-ordered; stop after the first working encoder.
            string? preferred = null;
            for (var i = 0; i < GpuEncoderPriority.Length; i++)
            {
                var encoder = GpuEncoderPriority[i];
                if (!encoderList.Contains(encoder, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!CanOpenGpuEncoder(ffmpegPath, ffmpegExists, encoder))
                {
                    continue;
                }

                preferred = encoder;
                break;
            }

            _preferredGpuEncoder = preferred;
            _availableGpuEncoders = preferred is null
                ? Array.Empty<string>()
                : new[] { preferred };
            _initialized = true;
        }
    }

    private static bool CanOpenGpuEncoder(string ffmpegPath, bool ffmpegExists, string encoder)
    {
        if (_gpuProbeOverride is not null)
        {
            return _gpuProbeOverride(ffmpegPath, encoder);
        }

        if (!ffmpegExists)
        {
            return false;
        }

        return ProbeGpuEncoder(ffmpegPath, encoder);
    }

    internal static bool ProbeGpuEncoder(string ffmpegPath, string encoder)
    {
        if (string.IsNullOrWhiteSpace(encoder))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Minimal open-probe: tiny frame, no audio, quiet logs, null muxer.
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("lavfi");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("color=c=black:s=16x16");
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add(encoder);
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("null");
            startInfo.ArgumentList.Add("-");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            // Drain without materializing full strings (failed probes can spam stderr).
            var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var stderrTask = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            if (!process.WaitForExit(GpuProbeTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort.
                }

                return false;
            }

            try
            {
                Task.WaitAll(stdoutTask, stderrTask);
            }
            catch
            {
                // Drain best-effort; exit code is authoritative.
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string QueryEncoders()
    {
        if (_encoderListOverride is not null)
        {
            return _encoderListOverride;
        }

        var ffmpegPath = FfmpegToolPaths.GetFfmpegPath();
        if (!File.Exists(ffmpegPath))
        {
            return string.Empty;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -encoders",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
