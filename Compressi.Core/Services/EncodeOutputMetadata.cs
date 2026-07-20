using Compressi.Core.Models;

namespace Compressi.Core.Services;

internal static class EncodeOutputMetadata
{
    public static VideoFile Create(CompressionJob job, FfmpegEncodePlan plan, long fileSizeBytes)
    {
        return new VideoFile
        {
            FilePath = plan.OutputPath,
            FileName = Path.GetFileName(plan.OutputPath),
            Width = plan.OutputWidth > 0 ? plan.OutputWidth : job.Source.Width,
            Height = plan.OutputHeight > 0 ? plan.OutputHeight : job.Source.Height,
            FrameRate = plan.OutputFrameRate > 0 ? plan.OutputFrameRate : job.Source.FrameRate,
            Duration = job.Source.Duration,
            VideoCodec = job.VideoCodec switch
            {
                VideoCodec.H264 => "h264",
                VideoCodec.Av1 => "av1",
                _ => throw new ArgumentOutOfRangeException(nameof(job), job.VideoCodec, "Unknown video codec."),
            },
            AudioCodec = job.Format == OutputFormat.WebM ? "opus" : "aac",
            ContainerFormat = ContainerName(job.Format),
            FileSizeBytes = fileSizeBytes,
        };
    }

    private static string ContainerName(OutputFormat format) => format switch
    {
        OutputFormat.Mp4 => "mp4",
        OutputFormat.Mkv => "mkv",
        OutputFormat.WebM => "webm",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown output format."),
    };
}
