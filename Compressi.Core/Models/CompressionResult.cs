namespace Compressi.Core.Models;

public sealed class CompressionResult
{
    public required VideoFile Source { get; init; }

    public required VideoFile Output { get; init; }

    public required string OutputPath { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public double AverageSpeedMegabytesPerSecond { get; init; }

    public double CompressionRatioPercent { get; init; }

    public long BytesSaved { get; init; }

    /// <summary>Optional note about how encoding ran (e.g. GPU→CPU fallback).</summary>
    public string? InfoNote { get; init; }

    public string BytesSavedDisplay => VideoFile.FormatFileSize(BytesSaved);

    public string CompressionRatioDisplay => $"{CompressionRatioPercent:0.#}%";

    public string AverageSpeedDisplay => $"{AverageSpeedMegabytesPerSecond:0.#} MB/s";
}
