namespace Compressi.Core.Models;

public sealed record EncodingProgress
{
    public double? ProgressPercent { get; init; }

    public TimeSpan? OutTime { get; init; }

    public long? OutputSizeBytes { get; init; }

    public double? SpeedMultiplier { get; init; }

    public bool IsFinished { get; init; }
}
