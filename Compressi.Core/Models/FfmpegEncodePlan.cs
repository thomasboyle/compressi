namespace Compressi.Core.Models;

public sealed class FfmpegEncodePlan
{
    public required string OutputPath { get; init; }

    public required IReadOnlyList<FfmpegEncodePass> Passes { get; init; }

    public int? TargetVideoBitrateKbps { get; init; }

    public int? AudioBitrateKbps { get; init; }

    public int OutputWidth { get; init; }

    public int OutputHeight { get; init; }

    /// <summary>Frozen output fps for filters / corrective passes; 0 means leave source timing.</summary>
    public int OutputFrameRate { get; init; }

    public bool UseHardwareAcceleration { get; init; }

    public string? GpuEncoder { get; init; }
}

public sealed class FfmpegEncodePass
{
    public required IReadOnlyList<string> Arguments { get; init; }

    public string? PassLogFilePrefix { get; init; }
}
