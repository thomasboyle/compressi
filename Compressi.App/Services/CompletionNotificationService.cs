using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Compressi_App.Services;

public static class CompletionNotificationService
{
    private static bool _registered;

    public static bool IsAvailable { get; private set; }

    public static void Initialize()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
            IsAvailable = true;
        }
        catch
        {
            _registered = false;
            IsAvailable = false;
        }
    }

    public static void ShowCompressionComplete(string fileName)
    {
        if (!_registered || !IsAvailable)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("Compression complete")
                .AddText(fileName)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            IsAvailable = false;
        }
    }
}
