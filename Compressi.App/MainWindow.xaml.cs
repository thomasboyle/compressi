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
    private readonly Dictionary<string, IAppPage> _pages = new(StringComparer.Ordinal);
    private readonly AppUpdateService _updateService = new();
    private bool _suppressSelectionChanged;
    private string? _currentTag;
    private bool _initialPageShown;

    public MainWindow()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        InitializeComponent();
        PerfProbe.MarkDuration("mainwindow_initialize_component", t0);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1331, 735));

        var backdropStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ConfigureSystemBackdrop();
        PerfProbe.MarkDuration("mainwindow_backdrop", backdropStart);

        var titleBarStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        PerfProbe.MarkDuration("mainwindow_titlebar", titleBarStart);

        var iconStart = System.Diagnostics.Stopwatch.GetTimestamp();
        AppWindow.SetIcon("Assets/AppIcon.ico");
        PerfProbe.MarkDuration("mainwindow_seticon", iconStart);

        var wireStart = System.Diagnostics.Stopwatch.GetTimestamp();
        App.HistoryViewModel.RerunRequested += (_, entry) => RerunCompression(entry);
        _updateService.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshUpdateBubble);
        Activated += MainWindow_Activated;
        RefreshUpdateBubble();

        _suppressSelectionChanged = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressSelectionChanged = false;
        PerfProbe.MarkDuration("mainwindow_wireup", wireStart);
    }

    /// <summary>
    /// Called after Activate so the window can appear before Compress page XAML is parsed.
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

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, PrecreateRemainingPages);
        // Always revalidate on launch so a release published after the last session is noticed.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = _updateService.CheckForUpdatesAsync(force: true));
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

    private void ConfigureSystemBackdrop()
    {
        // Cottagecore paper UI uses a solid cream surface; skip system acrylic/mica.
        SystemBackdrop = null;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _ = _updateService.CheckForUpdatesAsync();
        }
    }

    private void RefreshUpdateBubble()
    {
        var status = _updateService.Status;
        var update = _updateService.AvailableUpdate;
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
        UpdateBubbleProgress.Value = _updateService.DownloadProgress;
        UpdateBubbleDismissButton.IsEnabled = status is not AppUpdateStatus.Downloading and not AppUpdateStatus.Installing;
        UpdateBubbleActionButton.IsEnabled = status is AppUpdateStatus.Available or AppUpdateStatus.Failed;
        UpdateBubbleActionButton.Content = status switch
        {
            AppUpdateStatus.Downloading => $"Downloading {_updateService.DownloadProgress:0}%",
            AppUpdateStatus.Installing => "Installing...",
            AppUpdateStatus.Failed => "Retry install",
            _ => "Install update",
        };
    }

    private void UpdateBubbleDismissButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Release);
        _updateService.DismissAvailableUpdate();
    }

    private async void UpdateBubbleActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateService.Status is AppUpdateStatus.Downloading or AppUpdateStatus.Installing)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Press);
        try
        {
            await _updateService.DownloadAndInstallAsync().ConfigureAwait(true);
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

    private void PrecreateRemainingPages()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _ = GetOrCreatePage("History");
        _ = GetOrCreatePage("Settings");
        _ = GetOrCreatePage("About");
        PerfProbe.MarkDuration("precreate_remaining_pages", t0);
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
        }

        var page = GetOrCreatePage(tag);
        ContentHost.Content = (UIElement)page;
        page.Activate();
        _currentTag = tag;

        if (playSound)
        {
            UiSoundService.Play(UiSoundName.Page);
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
