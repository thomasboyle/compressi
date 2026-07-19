using Compressi.Core.Models;
using Compressi_App.Services;
using Compressi_App.Services.UiSounds;
using Compressi_App.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Compressi_App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, IAppPage> _pages = new(StringComparer.Ordinal);
    private bool _suppressSelectionChanged;
    private string? _currentTag;
    private bool _initialPageShown;

    public MainWindow()
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        InitializeComponent();
        PerfProbe.MarkDuration("mainwindow_initialize_component", t0);

        ConfigureSystemBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        App.HistoryViewModel.RerunRequested += (_, entry) => RerunCompression(entry);

        _suppressSelectionChanged = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressSelectionChanged = false;
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
        if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        else if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
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
