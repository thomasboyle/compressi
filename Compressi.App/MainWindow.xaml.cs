using Compressi.Core.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Compressi_App.Views;

namespace Compressi_App;

public sealed partial class MainWindow : Window
{
    private bool _suppressSelectionChanged;

    public MainWindow()
    {
        InitializeComponent();

        ConfigureSystemBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        App.HistoryViewModel.RerunRequested += (_, entry) => RerunCompression(entry);

        _suppressSelectionChanged = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressSelectionChanged = false;

        ContentFrame.Navigate(typeof(CompressPage));
    }

    public void RerunCompression(HistoryEntry entry)
    {
        _suppressSelectionChanged = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressSelectionChanged = false;

        if (ContentFrame.CurrentSourcePageType != typeof(CompressPage))
        {
            ContentFrame.Navigate(typeof(CompressPage));
        }

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

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        if (tag == "History")
        {
            App.HistoryViewModel.Refresh();
        }

        if (tag == "Settings")
        {
            App.SettingsViewModel.RefreshEncoderDetection();
        }

        var pageType = tag switch
        {
            "Compress" => typeof(CompressPage),
            "History" => typeof(HistoryPage),
            "Settings" => typeof(SettingsPage),
            "About" => typeof(AboutPage),
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown navigation tag."),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
