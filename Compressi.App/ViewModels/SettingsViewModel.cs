using System.ComponentModel;

using System.Runtime.CompilerServices;

using Compressi.Core.Models;

using Compressi.Core.Services;

using Compressi_App.Services;

using Compressi_App.Services.UiSounds;



namespace Compressi_App.ViewModels;



public sealed class SettingsViewModel : INotifyPropertyChanged

{

    private readonly SettingsStore _settingsStore;

    private AppSettings _settings;

    private int _encoderDetectionVersion;



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



    public bool LoadedFromDefaultsAfterError => _settingsStore.LoadedFromDefaultsAfterError;



    public bool NotificationsAvailable => CompletionNotificationService.IsAvailable;



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



    public bool UiSoundsEnabled

    {

        get => _settings.UiSoundsEnabled;

        set { _settings.UiSoundsEnabled = value; OnPropertyChanged(); }

    }



    public int UiSoundVolume

    {

        get => _settings.UiSoundVolume;

        set

        {

            _settings.UiSoundVolume = Math.Clamp(value, 0, 100);

            OnPropertyChanged();

            OnPropertyChanged(nameof(UiSoundVolumeDisplay));

        }

    }



    public string UiSoundVolumeDisplay => $"{UiSoundVolume}%";



    public string DetectedCpuEncoder => _settings.DetectedCpuEncoder ?? "Detecting...";



    public string DetectedGpuEncoder => _settings.DetectedGpuEncoder ?? "Detecting...";



    public bool NeedsEncoderDetection =>

        string.IsNullOrWhiteSpace(_settings.DetectedCpuEncoder)

        || string.IsNullOrWhiteSpace(_settings.DetectedGpuEncoder);



    public void Reload()

    {

        _settings = _settingsStore.Load();

        OnPropertyChanged(nameof(DefaultOutputFolder));

        OnPropertyChanged(nameof(DefaultPreset));

        OnPropertyChanged(nameof(HardwareAcceleration));

        OnPropertyChanged(nameof(ThreadCount));

        OnPropertyChanged(nameof(ThreadCountDisplay));

        OnPropertyChanged(nameof(Theme));

        OnPropertyChanged(nameof(NotifyOnCompletion));

        OnPropertyChanged(nameof(UiSoundsEnabled));

        OnPropertyChanged(nameof(UiSoundVolume));

        OnPropertyChanged(nameof(UiSoundVolumeDisplay));

        OnPropertyChanged(nameof(DetectedCpuEncoder));

        OnPropertyChanged(nameof(DetectedGpuEncoder));

        OnPropertyChanged(nameof(LoadedFromDefaultsAfterError));

        OnPropertyChanged(nameof(NotificationsAvailable));

    }



    /// <summary>

    /// Resolves encoder labels off the UI thread. Uses the catalog cache after the first probe

    /// instead of forcing a full re-probe on every Settings visit.

    /// </summary>

    public async Task EnsureEncoderDetectionAsync(bool persist = false)

    {

        var version = Interlocked.Increment(ref _encoderDetectionVersion);

        var (cpu, gpu) = await Task.Run(() =>

        {

            var cpuEncoder = FfmpegEncoderCatalog.GetCpuAv1Encoder();

            var gpuEncoder = FfmpegEncoderCatalog.GetPreferredGpuEncoder();

            return (cpuEncoder, gpuEncoder);

        }).ConfigureAwait(true);



        if (version != _encoderDetectionVersion)

        {

            return;

        }



        _settings.DetectedCpuEncoder = cpu;

        _settings.DetectedGpuEncoder = gpu ?? "None detected";

        OnPropertyChanged(nameof(DetectedCpuEncoder));

        OnPropertyChanged(nameof(DetectedGpuEncoder));



        if (persist)

        {

            _settingsStore.Save(_settings);

        }

    }



    public bool TrySave(out string? errorMessage)

    {

        try

        {

            if (!NotificationsAvailable)

            {

                _settings.NotifyOnCompletion = false;

            }



            _settingsStore.Save(_settings);

            ThemeService.ApplyTheme(_settings.Theme);

            UiSoundService.IsEnabled = _settings.UiSoundsEnabled;

            UiSoundService.VolumePercent = _settings.UiSoundVolume;

            SettingsSaved?.Invoke(this, EventArgs.Empty);

            errorMessage = null;

            return true;

        }

        catch (Exception ex)

        {

            errorMessage = $"Couldn't save settings: {ex.Message}";

            return false;

        }

    }



    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)

    {

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    }

}

