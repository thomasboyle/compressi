using System.Text.Json;
using Compressi.Core.Models;
using Compressi.Core.Services;

namespace Compressi_App.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Compressi",
            "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
            // Sync from the in-process catalog cache; do not Refresh() here — that re-spawns
            // ffmpeg probes on every Load() (including each compression start).
            SyncEncoderDetection(settings);
            return settings;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            DetectedCpuEncoder = FfmpegEncoderCatalog.GetCpuAv1Encoder(),
            DetectedGpuEncoder = FfmpegEncoderCatalog.GetPreferredGpuEncoder(),
        };
    }

    private static void SyncEncoderDetection(AppSettings settings)
    {
        settings.DetectedCpuEncoder = FfmpegEncoderCatalog.GetCpuAv1Encoder();
        settings.DetectedGpuEncoder = FfmpegEncoderCatalog.GetPreferredGpuEncoder();
    }
}
