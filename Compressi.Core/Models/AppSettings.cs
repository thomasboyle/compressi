namespace Compressi.Core.Models;

public sealed class AppSettings
{
    public string? DefaultOutputFolder { get; set; }

    public CompressionPreset DefaultPreset { get; set; } = CompressionPreset.Balanced;

    public bool HardwareAcceleration { get; set; } = true;

    public int ThreadCount { get; set; }

    public string Theme { get; set; } = "System";

    public bool NotifyOnCompletion { get; set; } = true;

    public bool UiSoundsEnabled { get; set; } = true;

    /// <summary>UI sound loudness from 0 (mute) to 100 (3× the original default level). Default 50 matches the original level.</summary>
    public int UiSoundVolume { get; set; } = 50;

    public string? DetectedGpuEncoder { get; set; }

    public string? DetectedCpuEncoder { get; set; }

    public int EffectiveThreadCount =>
        ThreadCount > 0 ? ThreadCount : Environment.ProcessorCount;
}
