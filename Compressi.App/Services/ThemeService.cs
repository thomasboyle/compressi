using Microsoft.UI.Xaml;

namespace Compressi_App.Services;

public static class ThemeService
{
    public static void ApplyTheme(string theme, FrameworkElement? rootElement = null)
    {
        var elementTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (rootElement is not null)
        {
            rootElement.RequestedTheme = elementTheme;
        }

        if (App.MainWindow?.Content is FrameworkElement windowRoot)
        {
            windowRoot.RequestedTheme = elementTheme;
        }
    }
}
