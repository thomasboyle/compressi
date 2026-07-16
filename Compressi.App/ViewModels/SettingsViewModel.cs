using System.ComponentModel;
using System.Runtime.CompilerServices;
using Compressi.Core.Models;
using Compressi.Core.Services;
using Compressi_App.Services;

namespace Compressi_App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore;
    private AppSettings _settings;

    public SettingsViewModel()
        : this(new SettingsStore())
    {
    }

    public SettingsViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsSaved;

    public AppSettings Settings => _settings;

    public string? DefaultOutputFolder
    {
        get => _settings.DefaultOutputFolder;
        set { _settings.DefaultOutputFolder = value; OnPropertyChanged(); }
    }

    public CompressionPreset DefaultPreset
    {
        get => _settings.DefaultPreset;
        set { _settings.DefaultPreset = value; OnPropertyChanged(); }
    }

    public bool HardwareAcceleration
    {
        get => _settings.HardwareAcceleration;
        set { _settings.HardwareAcceleration = value; OnPropertyChanged(); }
    }

    public int ThreadCount
    {
        get => _settings.ThreadCount;
        set { _settings.ThreadCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ThreadCountDisplay)); }
    }

    public string ThreadCountDisplay => _settings.ThreadCount <= 0
        ? $"Use all cores ({Environment.ProcessorCount})"
        : $"{_settings.ThreadCount} cores";

    public string Theme
    {
        get => _settings.Theme;
        set { _settings.Theme = value; OnPropertyChanged(); }
    }

    public bool NotifyOnCompletion
    {
        get => _settings.NotifyOnCompletion;
        set { _settings.NotifyOnCompletion = value; OnPropertyChanged(); }
    }

    public string DetectedCpuEncoder => _settings.DetectedCpuEncoder ?? FfmpegEncoderCatalog.GetCpuAv1Encoder();

    public string DetectedGpuEncoder => _settings.DetectedGpuEncoder ?? "None detected";

    public void RefreshEncoderDetection()
    {
        FfmpegEncoderCatalog.Refresh();
        _settings.DetectedCpuEncoder = FfmpegEncoderCatalog.GetCpuAv1Encoder();
        _settings.DetectedGpuEncoder = FfmpegEncoderCatalog.GetPreferredGpuEncoder();
        OnPropertyChanged(nameof(DetectedCpuEncoder));
        OnPropertyChanged(nameof(DetectedGpuEncoder));
    }

    public void Save()
    {
        _settingsStore.Save(_settings);
        ThemeService.ApplyTheme(_settings.Theme);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
