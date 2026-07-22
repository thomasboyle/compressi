using Compressi.Core.Services;
using Compressi_App.Services;
using Compressi_App.Services.UiSounds;
using Compressi_App.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Compressi_App;

public partial class App : Application
{
    private static readonly HistoryStore SharedHistoryStore = new();
    private static readonly SettingsStore SharedSettingsStore = new();
    private static HistoryViewModel? _historyViewModel;

    public static MainWindow? MainWindow { get; private set; }

    public static CompressViewModel CompressViewModel { get; } = CreateCompressViewModel();

    public static HistoryViewModel HistoryViewModel =>
        _historyViewModel ??= new HistoryViewModel(SharedHistoryStore);

    public static SettingsViewModel SettingsViewModel { get; } = CreateSettingsViewModel();

    static App()
    {
        PerfProbe.Mark("static_ctor_begin");
        // Do not touch HistoryViewModel here — keep it off the Compress TTI path.
        CompressViewModel.HistoryChanged += (_, _) => _historyViewModel?.MarkDirty();
        PerfProbe.Mark("static_ctor_end");
    }

    public App()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        InitializeComponent();
        PerfProbe.MarkDuration("app_initialize_component", t0);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        PerfProbe.Mark("on_launched_begin");

        // SettingsViewModel already loaded the snapshot during static init; reuse for theme/sounds.
        var settings = SettingsViewModel.Settings;
        ThemeService.ApplyTheme(settings.Theme);
        UiSoundService.IsEnabled = settings.UiSoundsEnabled;
        UiSoundService.VolumePercent = settings.UiSoundVolume;

        var windowStart = System.Diagnostics.Stopwatch.GetTimestamp();
        MainWindow = new MainWindow();
        PerfProbe.MarkDuration("main_window_ctor", windowStart);

        // Activate shell before parsing Compress page XAML so the window can appear sooner.
        MainWindow.Activate();
        PerfProbe.Mark("main_window_activate");

        MainWindow.ShowInitialPage();

        // Notification registration and sound synthesis are not required for first paint.
        MainWindow.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, InitializeDeferredServices);

        // FFmpeg encoder probes can take several seconds; never block first paint on them.
        _ = WarmupEncoderDetectionAsync();
    }

    private static void InitializeDeferredServices()
    {
        var notifStart = System.Diagnostics.Stopwatch.GetTimestamp();
        CompletionNotificationService.Initialize();
        PerfProbe.MarkDuration("completion_notification_init", notifStart);

        var soundWarmupStart = System.Diagnostics.Stopwatch.GetTimestamp();
        UiSoundService.Warmup();
        PerfProbe.MarkDuration("ui_sound_warmup_kickoff", soundWarmupStart);
    }

    private static CompressViewModel CreateCompressViewModel()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // Probe/encode path resolution (File.Exists) stays off TTI until first probe/encode.
        var vm = new CompressViewModel(
            new Lazy<IMediaProbeService>(() => new MediaProbeService()),
            new Lazy<IEncodingService>(() => new FfmpegEncodingService()),
            SharedHistoryStore,
            SharedSettingsStore);
        PerfProbe.MarkDuration("create_compress_viewmodel", t0);
        return vm;
    }

    private static SettingsViewModel CreateSettingsViewModel()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var vm = new SettingsViewModel(SharedSettingsStore);
        PerfProbe.MarkDuration("create_settings_viewmodel", t0);
        return vm;
    }

    private static async Task WarmupEncoderDetectionAsync()
    {
        if (!SettingsViewModel.NeedsEncoderDetection)
        {
            PerfProbe.Mark("encoder_warmup_skipped");
            return;
        }

        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await SettingsViewModel.EnsureEncoderDetectionAsync(persist: true).ConfigureAwait(true);
            CompressViewModel.ReloadSettings();
            PerfProbe.MarkDuration("encoder_warmup_complete", t0);
        }
        catch
        {
            PerfProbe.MarkDuration("encoder_warmup_failed", t0);
            // Best effort; encode/settings paths can still detect on demand.
        }
    }
}
