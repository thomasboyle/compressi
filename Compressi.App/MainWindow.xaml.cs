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
    private bool _deferredShellApplied;

    public MainWindow()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        InitializeComponent();
        PerfProbe.MarkDuration("mainwindow_initialize_component", t0);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1331, 735));

        var backdropStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ConfigureSystemBackdrop();
        PerfProbe.MarkDuration("mainwindow_backdrop", backdropStart);

        // History/update/timer/titlebar/grain are applied after tti (see ApplyDeferredShell).
        var wireStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _suppressSelectionChanged = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressSelectionChanged = false;
        PerfProbe.MarkDuration("mainwindow_wireup", wireStart);
    }

    /// <summary>
    /// Builds the initial Compress UI and shell chrome before <see cref="Window.Activate"/>.
    /// Calling this first avoids a blank/white first frame.
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

        // Title bar + icon must be applied before Activate; deferring them caused a white flash.
        ApplyDeferredShell();

        // Always revalidate on launch so a release published after the last session is noticed.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = UpdateService.CheckForUpdatesAsync(force: true));
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

    private AppUpdateService UpdateService =>
        _updateService ?? throw new InvalidOperationException("Deferred shell has not been applied yet.");

    private DispatcherQueueTimer PageEvictTimer =>
        _pageEvictTimer ?? throw new InvalidOperationException("Deferred shell has not been applied yet.");

    private void ConfigureSystemBackdrop()
    {
        // Cottagecore paper UI uses a solid cream surface; skip system acrylic/mica.
        SystemBackdrop = null;
    }

    private void ApplyDeferredShell()
    {
        if (_deferredShellApplied)
        {
            return;
        }

        _deferredShellApplied = true;

        // Realizes the x:Load="False" grain overlay (bitmap decode off the tti path).
        if (Content is FrameworkElement root)
        {
            _ = root.FindName(nameof(GrainOverlay));
        }

        var wireStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _updateService = new AppUpdateService();
        App.HistoryViewModel.RerunRequested += (_, entry) => RerunCompression(entry);
        _updateService.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshUpdateBubble);
        Activated += MainWindow_Activated;
        RefreshUpdateBubble();

        _pageEvictTimer = DispatcherQueue.CreateTimer();
        _pageEvictTimer.IsRepeating = false;
        _pageEvictTimer.Interval = PageEvictDelay;
        _pageEvictTimer.Tick += PageEvictTimer_Tick;
        PerfProbe.MarkDuration("mainwindow_deferred_wireup", wireStart);

        var titleBarStart = System.Diagnostics.Stopwatch.GetTimestamp();
        // Match paper surface so any pre-composition HWND chrome is cream, not white.
        var paper = Windows.UI.Color.FromArgb(0xFF, 0xE8, 0xDF, 0xD0);
        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = paper;
        titleBar.InactiveBackgroundColor = paper;
        titleBar.ButtonBackgroundColor = paper;
        titleBar.ButtonInactiveBackgroundColor = paper;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        PerfProbe.MarkDuration("mainwindow_titlebar", titleBarStart);

        var iconStart = System.Diagnostics.Stopwatch.GetTimestamp();
        AppWindow.SetIcon("Assets/AppIcon.ico");
        PerfProbe.MarkDuration("mainwindow_seticon", iconStart);
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
