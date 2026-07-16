namespace Compressi.Core.Models;

public sealed class VideoFile
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Average frames per second from probe; 0 when unknown.</summary>
    public double FrameRate { get; init; }

    public required TimeSpan Duration { get; init; }

    public required string VideoCodec { get; init; }

    public string? AudioCodec { get; init; }

    public required string ContainerFormat { get; init; }

    public required long FileSizeBytes { get; init; }

    public string ResolutionDisplay => $"{Width} × {Height}";

    public string DurationDisplay => FormatDuration(Duration);

    public string DurationClockDisplay => FormatDurationClock(Duration);

    public string CodecFormatDisplay => FormatCodecDisplay(VideoCodec, ContainerFormat);

    public string FileSizeDisplay => FormatFileSize(FileSizeBytes);

    public string MetadataLine => $"{ResolutionDisplay} • {DurationDisplay} • {FileSizeDisplay}";

    public static string FormatDurationClock(TimeSpan duration) =>
        $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";

    public static string FormatCodecDisplay(string videoCodec, string containerFormat)
    {
        var codec = videoCodec.ToLowerInvariant() switch
        {
            "h264" => "H.264",
            "hevc" or "h265" => "H.265",
            "av1" => "AV1",
            "vp9" => "VP9",
            _ => videoCodec.ToUpperInvariant(),
        };

        return $"{codec} / {containerFormat.ToUpperInvariant()}";
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    public static string FormatFileSize(long bytes)
    {
        const double mb = 1024 * 1024;
        const double gb = 1024 * mb;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.##} GB";
        }

        return $"{bytes / mb:0.#} MB";
    }
}
