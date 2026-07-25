using Compressi.Core.Models;
using Compressi_App.Services;
using Compressi_App.Services.UiSounds;
using Compressi_App.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Compressi_App;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan PageEvictDelay = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, IAppPage> _pages = new(StringComparer.Ordinal);
    private AppUpdateService? _updateService;
    private DispatcherQueueTimer? _pageEvictTimer;
    private bool _suppressSelectionChanged;
    private string? _currentTag;
    private string? _pendingEvictTag;
    private bool _initialPageShown;
    private bool _shellChromeApplied;
    private bool _revealed;

    public MainWindow()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        InitializeComponent();
        PerfProbe.MarkDuration("mainwindow_initialize_component", t0);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1331, 735));

        // Cloak before any Activate so DWM never presents the default white HWND frame.
        // Cream class brush covers any transient HWND erase if cloak is briefly ineffective.
        WindowStartupCloak.SetCloaked(this, cloaked: true);
        WindowStartupCloak.ApplyPaperBackground(this);

        var backdropStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ConfigureSystemBackdrop();
        PerfProbe.MarkDuration("mainwindow_backdrop", backdropStart);

        var wireStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _suppressSelectionChanged = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressSelectionChanged = false;
        PerfProbe.MarkDuration("mainwindow_wireup", wireStart);
    }

    /// <summary>
    /// Builds the initial Compress UI tree (full interactive page, no skeleton).
    /// </summary>
    public void ShowInitialPage()
    {
        if (_initialPageShown)
        {
            return;
        }

        _initialPageShown = true;
        var showStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ShowPage("Compress", playSound: false);
        PerfProbe.MarkDuration("show_compress_page", showStart);
        PerfProbe.Mark("tti");
    }

    /// <summary>
    /// Uncloaks immediately. Call only after the full Compress UI tree is in place.
    /// Title-bar customization runs right after via High priority — first touch of that
    /// WinUI surface is ~130 ms and must not block DWM uncloak.
    /// </summary>
    public void RevealNow()
    {
        if (_revealed)
        {
            return;
        }

        _revealed = true;
        WindowStartupCloak.SetCloaked(this, cloaked: false);
        PerfProbe.Mark("window_revealed");

        // High: custom title bar + cream caption buttons before the next input frame.
        // Low: update checks / notifications / sounds after first paint settles.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyShellChrome);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RunPostRevealWork);
    }

    private void ApplyShellChrome()
    {
        if (_shellChromeApplied)
        {
            return;
        }

        _shellChromeApplied = true;

        // Realizes the x:Load="False" grain overlay (decorative; safe one frame late).
        if (Content is FrameworkElement root)
        {
            _ = root.FindName(nameof(GrainOverlay));
        }

        var titleBarStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.Visibility = Visibility.Visible;
        var paper = Windows.UI.Color.FromArgb(0xFF, 0xE8, 0xDF, 0xD0);
        AppWindow.TitleBar.ButtonBackgroundColor = paper;
        PerfProbe.MarkDuration("mainwindow_titlebar", titleBarStart);

        var iconStart = System.Diagnostics.Stopwatch.GetTimestamp();
        AppWindow.SetIcon("Assets/AppIcon.ico");
        PerfProbe.MarkDuration("mainwindow_seticon", iconStart);
    }

    private void RunPostRevealWork()
    {
        var wireStart = System.Diagnostics.Stopwatch.GetTimestamp();
        App.HistoryViewModel.RerunRequested += (_, entry) => RerunCompression(entry);
        UpdateService.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshUpdateBubble);
        Activated += MainWindow_Activated;
        RefreshUpdateBubble();
        PerfProbe.MarkDuration("mainwindow_deferred_wireup", wireStart);

        // Always revalidate on launch so a release published after the last session is noticed.
        _ = UpdateService.CheckForUpdatesAsync(force: true);
        App.InitializeDeferredServices();
    }

    public void NavigateToCompress()
    {
        _suppressSelectionChanged = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressSelectionChanged = false;
        ShowPage("Compress");
    }

    public void RerunCompression(HistoryEntry entry)
    {
        NavigateToCompress();
        App.CompressViewModel.RequestRerun(entry);
    }

    // Created on demand: reading the update cache is file + JSON work that must not run
    // before the window is revealed.
    private AppUpdateService UpdateService => _updateService ??= new AppUpdateService();

    private DispatcherQueueTimer PageEvictTimer
    {
        get
        {
            if (_pageEvictTimer is null)
            {
                _pageEvictTimer = DispatcherQueue.CreateTimer();
                _pageEvictTimer.IsRepeating = false;
                _pageEvictTimer.Interval = PageEvictDelay;
                _pageEvictTimer.Tick += PageEvictTimer_Tick;
            }

            return _pageEvictTimer;
        }
    }

    private void ConfigureSystemBackdrop()
    {
        // Cottagecore paper UI uses a solid cream surface; skip system acrylic/mica.
        SystemBackdrop = null;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            // Non-forced: AppUpdateService enforces a multi-hour recheck interval.
            _ = UpdateService.CheckForUpdatesAsync();
        }
    }

    private void RefreshUpdateBubble()
    {
        var updateService = UpdateService;
        var status = updateService.Status;
        var update = updateService.AvailableUpdate;
        var show = update is not null
            || status is AppUpdateStatus.Downloading or AppUpdateStatus.Installing;

        UpdateBubble.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            return;
        }

        UpdateBubbleVersionText.Text = update is null ? string.Empty : $"v{update.Version}";
        UpdateBubbleProgress.Visibility = status is AppUpdateStatus.Downloading or AppUpdateStatus.Installing
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateBubbleProgress.Value = updateService.DownloadProgress;
        UpdateBubbleDismissButton.IsEnabled = status is not AppUpdateStatus.Downloading and not AppUpdateStatus.Installing;
        UpdateBubbleActionButton.IsEnabled = status is AppUpdateStatus.Available or AppUpdateStatus.Failed;
        UpdateBubbleActionButton.Content = status switch
        {
            AppUpdateStatus.Downloading => $"Downloading {updateService.DownloadProgress:0}%",
            AppUpdateStatus.Installing => "Installing...",
            AppUpdateStatus.Failed => "Retry install",
            _ => "Install update",
        };
    }

    private void UpdateBubbleDismissButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Release);
        UpdateService.DismissAvailableUpdate();
    }

    private async void UpdateBubbleActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateService.Status is AppUpdateStatus.Downloading or AppUpdateStatus.Installing)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Press);
        try
        {
            await UpdateService.DownloadAndInstallAsync().ConfigureAwait(true);
            Close();
        }
        catch
        {
            RefreshUpdateBubble();
        }
    }

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        if (string.Equals(_currentTag, tag, StringComparison.Ordinal))
        {
            return;
        }

        if (_currentTag is not null
            && _pages.TryGetValue(_currentTag, out var previous)
            && previous is SettingsPage settingsPage
            && !await settingsPage.ConfirmLeaveAsync())
        {
            RestoreSelection(_currentTag);
            return;
        }

        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        ShowPage(tag);
        PerfProbe.MarkDuration("nav_show_page", t0, tag);
    }

    private void RestoreSelection(string tag)
    {
        _suppressSelectionChanged = true;
        foreach (var menuItem in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (menuItem.Tag is string itemTag && string.Equals(itemTag, tag, StringComparison.Ordinal))
            {
                NavView.SelectedItem = menuItem;
                break;
            }
        }

        _suppressSelectionChanged = false;
    }

    private void ShowPage(string tag, bool playSound = true)
    {
        if (string.Equals(_currentTag, tag, StringComparison.Ordinal))
        {
            return;
        }

        if (_currentTag is not null && _pages.TryGetValue(_currentTag, out var previous))
        {
            previous.Deactivate();
            SchedulePageEviction(_currentTag);
        }

        CancelPageEviction(tag);

        var page = GetOrCreatePage(tag);
        ContentHost.Content = (UIElement)page;
        page.Activate();
        _currentTag = tag;

        if (playSound)
        {
            UiSoundService.Play(UiSoundName.Page);
        }
    }

    private void SchedulePageEviction(string tag)
    {
        // Keep Compress warm; soft-evict other pages after idle to reclaim visual-tree memory.
        if (string.Equals(tag, "Compress", StringComparison.Ordinal))
        {
            return;
        }

        _pendingEvictTag = tag;
        var timer = PageEvictTimer;
        timer.Stop();
        timer.Start();
    }

    private void CancelPageEviction(string tag)
    {
        if (string.Equals(_pendingEvictTag, tag, StringComparison.Ordinal))
        {
            _pageEvictTimer?.Stop();
            _pendingEvictTag = null;
        }
    }

    private void PageEvictTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var tag = _pendingEvictTag;
        _pendingEvictTag = null;
        if (tag is null || string.Equals(_currentTag, tag, StringComparison.Ordinal))
        {
            return;
        }

        if (_pages.Remove(tag))
        {
            PerfProbe.Mark("page_evicted", tag);
        }
    }

    private IAppPage GetOrCreatePage(string tag)
    {
        if (_pages.TryGetValue(tag, out var existing))
        {
            return existing;
        }

        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        IAppPage page = tag switch
        {
            "Compress" => new CompressPage(),
            "History" => new HistoryPage(),
            "Settings" => new SettingsPage(),
            "About" => new AboutPage(),
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown navigation tag."),
        };
        PerfProbe.MarkDuration("create_page", t0, tag);

        _pages[tag] = page;
        return page;
    }
}
