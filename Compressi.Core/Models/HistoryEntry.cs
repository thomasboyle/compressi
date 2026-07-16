namespace Compressi.Core.Models;

public sealed class HistoryEntry
{
    public long Id { get; init; }

    public required string SourceName { get; init; }

    public required string SourcePath { get; init; }

    public string? OutputPath { get; init; }

    public required CompressionPreset Preset { get; init; }

    public required OutputFormat Format { get; init; }

    public required CompressionJobStatus Status { get; init; }

    public long OriginalSizeBytes { get; init; }

    public long CompressedSizeBytes { get; init; }

    public double CompressionRatioPercent { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("g");

    public string OriginalSizeDisplay => VideoFile.FormatFileSize(OriginalSizeBytes);

    public string CompressedSizeDisplay => VideoFile.FormatFileSize(CompressedSizeBytes);

    public string RatioDisplay => $"{CompressionRatioPercent:0.#}%";

    public string StatusDisplay => Status switch
    {
        CompressionJobStatus.Completed => "Completed",
        CompressionJobStatus.Failed => "Failed",
        CompressionJobStatus.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(Status), Status, "Unknown status."),
    };
}
