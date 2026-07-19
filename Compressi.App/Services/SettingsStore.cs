using System.Text.Json;
using Compressi.Core.Models;

namespace Compressi_App.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly object _gate = new();
    private AppSettings? _snapshot;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Compressi",
            "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
    }

    public bool LoadedFromDefaultsAfterError { get; private set; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (_snapshot is not null)
            {
                return Clone(_snapshot);
            }

            LoadedFromDefaultsAfterError = false;

            if (!File.Exists(_settingsPath))
            {
                _snapshot = CreateDefault();
                return Clone(_snapshot);
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                _snapshot = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
            }
            catch
            {
                LoadedFromDefaultsAfterError = true;
                _snapshot = CreateDefault();
            }

            return Clone(_snapshot);
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
            _snapshot = Clone(settings);
            LoadedFromDefaultsAfterError = false;
        }
    }

    private static AppSettings CreateDefault() => new();

    private static AppSettings Clone(AppSettings source) => new()
    {
        DefaultOutputFolder = source.DefaultOutputFolder,
        DefaultPreset = source.DefaultPreset,
        HardwareAcceleration = source.HardwareAcceleration,
        ThreadCount = source.ThreadCount,
        Theme = source.Theme,
        NotifyOnCompletion = source.NotifyOnCompletion,
        UiSoundsEnabled = source.UiSoundsEnabled,
        UiSoundVolume = source.UiSoundVolume,
        DetectedGpuEncoder = source.DetectedGpuEncoder,
        DetectedCpuEncoder = source.DetectedCpuEncoder,
    };
}
