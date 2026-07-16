namespace Compressi.Core.Models;

public sealed class AppSettings
{
    public string? DefaultOutputFolder { get; set; }

    public CompressionPreset DefaultPreset { get; set; } = CompressionPreset.Balanced;

    public bool HardwareAcceleration { get; set; } = true;

    public int ThreadCount { get; set; }

    public string Theme { get; set; } = "System";

    public bool NotifyOnCompletion { get; set; } = true;

    public string? DetectedGpuEncoder { get; set; }

    public string? DetectedCpuEncoder { get; set; }

    public int EffectiveThreadCount =>
        ThreadCount > 0 ? ThreadCount : Environment.ProcessorCount;
}
