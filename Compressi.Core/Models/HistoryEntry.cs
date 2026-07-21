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

    public required string CreatedAtDisplay { get; init; }

    public required string OriginalSizeDisplay { get; init; }

    public required string CompressedSizeDisplay { get; init; }

    public required string RatioDisplay { get; init; }

    public required string SizeSummaryDisplay { get; init; }

    public required string StatusDisplay { get; init; }

    public static HistoryEntry Create(
        long id,
        string sourceName,
        string sourcePath,
        string? outputPath,
        CompressionPreset preset,
        OutputFormat format,
        CompressionJobStatus status,
        long originalSizeBytes,
        long compressedSizeBytes,
        double compressionRatioPercent,
        DateTimeOffset createdAt)
    {
        var originalSizeDisplay = VideoFile.FormatFileSize(originalSizeBytes);
        var compressedSizeDisplay = VideoFile.FormatFileSize(compressedSizeBytes);
        var ratioDisplay = $"{compressionRatioPercent:0.#}%";

        return new HistoryEntry
        {
            Id = id,
            SourceName = sourceName,
            SourcePath = sourcePath,
            OutputPath = outputPath,
            Preset = preset,
            Format = format,
            Status = status,
            OriginalSizeBytes = originalSizeBytes,
            CompressedSizeBytes = compressedSizeBytes,
            CompressionRatioPercent = compressionRatioPercent,
            CreatedAt = createdAt,
            CreatedAtDisplay = createdAt.ToLocalTime().ToString("g"),
            OriginalSizeDisplay = originalSizeDisplay,
            CompressedSizeDisplay = compressedSizeDisplay,
            RatioDisplay = ratioDisplay,
            SizeSummaryDisplay = $"{originalSizeDisplay} → {compressedSizeDisplay} ({ratioDisplay})",
            StatusDisplay = FormatStatus(status),
        };
    }

    private static string FormatStatus(CompressionJobStatus status) => status switch
    {
        CompressionJobStatus.Completed => "Completed",
        CompressionJobStatus.Failed => "Failed",
        CompressionJobStatus.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status."),
    };
}
