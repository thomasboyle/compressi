namespace Compressi.Core.Models;

public sealed class CompressionJob
{
    public required VideoFile Source { get; init; }

    public required CompressionPreset Preset { get; init; }

    public required OutputFormat Format { get; init; }

    public AdvancedEncodingOptions? Advanced { get; init; }

    public int ThreadCount { get; init; } = Environment.ProcessorCount;

    public bool HardwareAccelerationEnabled { get; init; } = true;

    public string? GpuEncoder { get; init; }

    public string? EncodingInfoNote { get; init; }
}
