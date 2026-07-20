using Compressi.Core.Models;

namespace Compressi.Core.Services;

public static class CompressionPresetResolver
{
    public static FfmpegEncodePlan Resolve(CompressionJob job)
    {
        EncodePreflightValidator.Validate(job);

        return job.Preset switch
        {
            CompressionPreset.Balanced => BuildQualityPlan(job, EncodingConstants.BalancedCrf, EncodingConstants.BalancedPreset, preferPassthroughAudio: true),
            CompressionPreset.Ultra => BuildUltraPlan(job),
            CompressionPreset.EightMB => BuildEightMbPlan(job),
            _ => throw new ArgumentOutOfRangeException(nameof(job), job.Preset, "Unknown compression preset."),
        };
    }

    private static FfmpegEncodePlan BuildUltraPlan(CompressionJob job)
    {
        var outputPath = BuildOutputPath(job);
        var args = CreateBaseArgs(job);
        var (width, height) = ResolveOutputDimensions(job);

        AppendUltraVideoFilters(args, job);
        AppendEncoderArgs(args, job, EncodingConstants.UltraCrf, EncodingConstants.UltraPreset);
        AppendTranscodedAudioArgs(args, job, EncodingConstants.UltraAudioBitrateKbps);

        AppendProgressAndOutput(args, outputPath);
        return SinglePassPlan(outputPath, args, job, width, height);
    }

    private static FfmpegEncodePlan BuildQualityPlan(
        CompressionJob job,
        int crf,
        int preset,
        bool preferPassthroughAudio)
    {
        var outputPath = BuildOutputPath(job);
        var args = CreateBaseArgs(job);
        var (width, height) = ResolveOutputDimensions(job);

        AppendVideoFilters(args, job, autoDownscale1080: false);
        AppendEncoderArgs(args, job, crf, preset);
        if (preferPassthroughAudio)
        {
            AppendAudioArgs(args, job);
        }
        else
        {
            AppendTranscodedAudioArgs(args, job, EncodingConstants.DefaultAudioBitrateKbps);
        }

        AppendProgressAndOutput(args, outputPath);
        return SinglePassPlan(outputPath, args, job, width, height);
    }

    private static FfmpegEncodePlan BuildEightMbPlan(CompressionJob job)
    {
        var eightMb = EightMbBitrateResolver.Resolve(job.Source, job.Advanced);
        if (!eightMb.IsViable)
        {
            throw new InvalidOperationException(eightMb.FailureReason);
        }

        var outputPath = BuildOutputPath(job);
        var passLogFile = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
            $"ffmpeg2pass-{Guid.NewGuid():N}");

        var passOne = CreateBaseArgs(job);
        ApplyEightMbVideoFilters(passOne, job, eightMb.OutputWidth, eightMb.OutputHeight, eightMb.OutputFrameRate);
        AppendEightMbBitrateArgs(passOne, job, eightMb.VideoBitrateKbps, passNumber: 1, passLogFile);
        AppendTranscodedAudioArgs(passOne, job, eightMb.AudioBitrateKbps);
        AppendProgressReporting(passOne);
        AppendNullOutput(passOne, job.Format);

        var passTwo = CreateBaseArgs(job);
        ApplyEightMbVideoFilters(passTwo, job, eightMb.OutputWidth, eightMb.OutputHeight, eightMb.OutputFrameRate);
        AppendEightMbBitrateArgs(passTwo, job, eightMb.VideoBitrateKbps, passNumber: 2, passLogFile);
        AppendTranscodedAudioArgs(passTwo, job, eightMb.AudioBitrateKbps);
        AppendProgressAndOutput(passTwo, outputPath);

        return new FfmpegEncodePlan
        {
            OutputPath = outputPath,
            Passes =
            [
                new FfmpegEncodePass { Arguments = passOne, PassLogFilePrefix = passLogFile },
                new FfmpegEncodePass { Arguments = passTwo, PassLogFilePrefix = passLogFile },
            ],
            TargetVideoBitrateKbps = eightMb.VideoBitrateKbps,
            AudioBitrateKbps = eightMb.AudioBitrateKbps,
            OutputWidth = eightMb.OutputWidth,
            OutputHeight = eightMb.OutputHeight,
            OutputFrameRate = eightMb.OutputFrameRate,
            UseHardwareAcceleration = false,
            GpuEncoder = null,
        };
    }

    /// <summary>
    /// Builds a single-pass corrective encode from frozen plan decisions (no re-resolve).
    /// </summary>
    public static FfmpegEncodePass BuildEightMbCorrectivePass(
        CompressionJob job,
        FfmpegEncodePlan plan,
        int adjustedBitrateKbps)
    {
        var args = CreateBaseArgs(job);
        ApplyEightMbVideoFilters(args, job, plan.OutputWidth, plan.OutputHeight, plan.OutputFrameRate);
        AppendEightMbSinglePassBitrateArgs(args, job, adjustedBitrateKbps);

        var audioKbps = plan.AudioBitrateKbps ?? EncodingConstants.EightMbAudioBitrateKbps;
        AppendTranscodedAudioArgs(args, job, audioKbps);
        AppendProgressAndOutput(args, plan.OutputPath);

        return new FfmpegEncodePass { Arguments = args };
    }

    private static FfmpegEncodePlan SinglePassPlan(
        string outputPath,
        List<string> args,
        CompressionJob job,
        int width,
        int height)
    {
        var useGpu = ShouldUseGpu(job);
        return new FfmpegEncodePlan
        {
            OutputPath = outputPath,
            Passes = [new FfmpegEncodePass { Arguments = args }],
            OutputWidth = width,
            OutputHeight = height,
            OutputFrameRate = job.Advanced?.FrameRateOverride ?? 0,
            UseHardwareAcceleration = useGpu,
            GpuEncoder = useGpu ? job.GpuEncoder : null,
            AudioBitrateKbps = job.Advanced?.AudioBitrateKbps,
        };
    }

    internal static (int Width, int Height) ResolveOutputDimensions(CompressionJob job)
    {
        if (ResolutionParser.TryGetOverride(job.Advanced, out var width, out var height))
        {
            return (width, height);
        }

        if (job.Preset == CompressionPreset.Ultra
            && job.Source.Height > EncodingConstants.UltraMaxHeight)
        {
            var scaledWidth = Math.Max(2, (int)Math.Round(job.Source.Width * (1080d / job.Source.Height)));
            if (scaledWidth % 2 != 0)
            {
                scaledWidth--;
            }

            return (scaledWidth, EncodingConstants.UltraMaxHeight);
        }

        return (job.Source.Width, job.Source.Height);
    }

    private static List<string> CreateBaseArgs(CompressionJob job)
    {
        return
        [
            "-y",
            "-hide_banner",
            "-i", job.Source.FilePath,
            "-map", "0:v:0",
            "-map", "0:a:0?",
        ];
    }

    private static bool ShouldUseGpu(CompressionJob job) =>
        job.VideoCodec == VideoCodec.Av1
        && job.HardwareAccelerationEnabled
        && !string.IsNullOrWhiteSpace(job.GpuEncoder)
        && job.Preset != CompressionPreset.EightMB;

    private static void AppendEncoderArgs(List<string> args, CompressionJob job, int av1Crf, int av1Preset)
    {
        if (job.VideoCodec == VideoCodec.H264)
        {
            var (crf, preset) = ResolveH264Quality(job.Preset);
            VideoEncoderArgumentBuilder.AppendCpuH264Args(args, job.ThreadCount, crf, preset);
            return;
        }

        if (ShouldUseGpu(job))
        {
            VideoEncoderArgumentBuilder.AppendGpuAv1Args(args, job.GpuEncoder!, av1Crf);
            return;
        }

        var tiles = VideoEncoderArgumentBuilder.GetTileConfig(job.Source.Width, job.Source.Height);
        VideoEncoderArgumentBuilder.AppendCpuAv1Args(
            args,
            job.ThreadCount,
            av1Crf,
            av1Preset,
            tiles?.tileRows,
            tiles?.tileColumns);
    }

    private static void AppendEightMbBitrateArgs(
        List<string> args,
        CompressionJob job,
        int bitrateKbps,
        int passNumber,
        string passLogFile)
    {
        if (job.VideoCodec == VideoCodec.H264)
        {
            VideoEncoderArgumentBuilder.AppendCpuH264BitrateArgs(
                args,
                job.ThreadCount,
                bitrateKbps,
                EncodingConstants.EightMbH264Preset,
                passNumber,
                passLogFile);
            return;
        }

        VideoEncoderArgumentBuilder.AppendCpuAv1BitrateArgs(
            args,
            job.ThreadCount,
            bitrateKbps,
            EncodingConstants.EightMbPreset,
            passNumber,
            passLogFile);
    }

    private static void AppendEightMbSinglePassBitrateArgs(
        List<string> args,
        CompressionJob job,
        int bitrateKbps)
    {
        if (job.VideoCodec == VideoCodec.H264)
        {
            VideoEncoderArgumentBuilder.AppendCpuH264SinglePassBitrateArgs(
                args,
                job.ThreadCount,
                bitrateKbps,
                EncodingConstants.EightMbH264Preset);
            return;
        }

        VideoEncoderArgumentBuilder.AppendCpuAv1SinglePassBitrateArgs(
            args,
            job.ThreadCount,
            bitrateKbps,
            EncodingConstants.EightMbPreset);
    }

    private static (int Crf, string Preset) ResolveH264Quality(CompressionPreset preset) => preset switch
    {
        CompressionPreset.Balanced => (EncodingConstants.BalancedH264Crf, EncodingConstants.BalancedH264Preset),
        CompressionPreset.Ultra => (EncodingConstants.UltraH264Crf, EncodingConstants.UltraH264Preset),
        CompressionPreset.EightMB => (EncodingConstants.BalancedH264Crf, EncodingConstants.EightMbH264Preset),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown compression preset."),
    };

    private static void AppendUltraVideoFilters(List<string> args, CompressionJob job)
    {
        AppendVideoFilters(args, job, autoDownscale1080: job.Source.Height > EncodingConstants.UltraMaxHeight);
    }

    private static void AppendVideoFilters(List<string> args, CompressionJob job, bool autoDownscale1080)
    {
        var filters = new List<string>();

        if (ResolutionParser.TryGetOverride(job.Advanced, out var width, out var height))
        {
            filters.Add($"scale={width}:{height}");
        }
        else if (autoDownscale1080 && job.Source.Height > EncodingConstants.UltraMaxHeight)
        {
            var scaledWidth = Math.Max(2, (int)Math.Round(job.Source.Width * (1080d / job.Source.Height)));
            if (scaledWidth % 2 != 0)
            {
                scaledWidth--;
            }

            filters.Add($"scale={scaledWidth}:1080");
        }

        if (job.Advanced?.FrameRateOverride is int frameRate && frameRate > 0)
        {
            filters.Add($"fps={frameRate}");
        }

        if (filters.Count > 0)
        {
            args.Add("-vf");
            args.Add(string.Join(',', filters));
        }
    }

    public static void ApplyEightMbVideoFilters(
        List<string> args,
        CompressionJob job,
        int outputWidth,
        int outputHeight,
        int outputFrameRate)
    {
        var filters = new List<string>();

        if (outputWidth != job.Source.Width || outputHeight != job.Source.Height)
        {
            filters.Add($"scale={outputWidth}:{outputHeight}");
        }

        if (outputFrameRate > 0)
        {
            filters.Add($"fps={outputFrameRate}");
        }

        if (filters.Count > 0)
        {
            args.Add("-vf");
            args.Add(string.Join(',', filters));
        }
    }

    private static void AppendAudioArgs(List<string> args, CompressionJob job)
    {
        var format = job.Format;
        var audioCodec = job.Source.AudioCodec;
        var keepOriginal = job.Advanced?.KeepOriginalAudio == true;
        var audioBitrate = job.Advanced?.AudioBitrateKbps ?? EncodingConstants.DefaultAudioBitrateKbps;

        if (format == OutputFormat.WebM)
        {
            if (keepOriginal && string.Equals(audioCodec, "opus", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("-c:a");
                args.Add("copy");
                return;
            }

            args.Add("-c:a");
            args.Add("libopus");
            args.Add("-b:a");
            args.Add($"{audioBitrate}k");
            return;
        }

        if (keepOriginal && CanCopyAudioForContainer(format, audioCodec))
        {
            args.Add("-c:a");
            args.Add("copy");
            return;
        }

        if (CanCopyAudioForContainer(format, audioCodec))
        {
            args.Add("-c:a");
            args.Add("copy");
            return;
        }

        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add($"{audioBitrate}k");
    }

    private static void AppendTranscodedAudioArgs(List<string> args, CompressionJob job, int audioBitrateKbps)
    {
        if (job.Format == OutputFormat.WebM)
        {
            args.Add("-c:a");
            args.Add("libopus");
            args.Add("-b:a");
            args.Add($"{audioBitrateKbps}k");
            return;
        }

        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add($"{audioBitrateKbps}k");
    }

    private static bool CanCopyAudioForContainer(OutputFormat format, string? audioCodec)
    {
        if (string.IsNullOrWhiteSpace(audioCodec))
        {
            return false;
        }

        return format switch
        {
            OutputFormat.Mp4 => audioCodec is "aac" or "mp3",
            OutputFormat.Mkv => audioCodec is "aac" or "mp3" or "opus" or "flac",
            OutputFormat.WebM => string.Equals(audioCodec, "opus", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static void AppendProgressAndOutput(List<string> args, string outputPath)
    {
        AppendProgressReporting(args);
        args.Add(outputPath);
    }

    private static void AppendProgressReporting(List<string> args)
    {
        args.Add("-progress");
        args.Add("pipe:1");
        args.Add("-nostats");
    }

    public static void AppendNullOutput(List<string> args, OutputFormat format)
    {
        switch (format)
        {
            case OutputFormat.Mp4:
                args.Add("-f");
                args.Add("mp4");
                break;
            case OutputFormat.Mkv:
                args.Add("-f");
                args.Add("matroska");
                break;
            case OutputFormat.WebM:
                args.Add("-f");
                args.Add("webm");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown output format.");
        }

        args.Add("NUL");
    }

    public static string ResolveOutputPath(CompressionJob job, bool ensureDirectoryExists = false)
    {
        var extension = job.Format switch
        {
            OutputFormat.Mp4 => ".mp4",
            OutputFormat.Mkv => ".mkv",
            OutputFormat.WebM => ".webm",
            _ => throw new ArgumentOutOfRangeException(nameof(job), job.Format, "Unknown output format."),
        };

        var sourceName = Path.GetFileNameWithoutExtension(job.Source.FilePath);
        var pattern = job.Advanced?.OutputFilenamePattern;
        var outputName = string.IsNullOrWhiteSpace(pattern)
            ? $"{sourceName}_compressed"
            : pattern
                .Replace("{name}", sourceName, StringComparison.OrdinalIgnoreCase)
                .Replace("{ext}", extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase);

        var outputDirectory = job.Advanced?.OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Path.GetDirectoryName(job.Source.FilePath) ?? Environment.CurrentDirectory;
        }

        if (ensureDirectoryExists)
        {
            Directory.CreateDirectory(outputDirectory);
        }

        return Path.Combine(outputDirectory, outputName + extension);
    }

    private static string BuildOutputPath(CompressionJob job) =>
        ResolveOutputPath(job, ensureDirectoryExists: true);
}
