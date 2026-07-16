using Compressi_App.Services;
using Compressi_App.ViewModels;
using Microsoft.UI.Xaml;

namespace Compressi_App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public static CompressViewModel CompressViewModel { get; } = new();

    public static HistoryViewModel HistoryViewModel { get; } = new();

    public static SettingsViewModel SettingsViewModel { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = SettingsViewModel.Settings;
        ThemeService.ApplyTheme(settings.Theme);

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
