using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Compressi_App.Services;

/// <summary>
/// Hides a WinUI window from DWM until XAML has a chance to paint, avoiding the
/// default white/black HWND flash on Activate (microsoft-ui-xaml#7892 / #10259).
/// </summary>
internal static class WindowStartupCloak
{
    private const int DwmwaCloak = 13;

    public static void SetCloaked(Microsoft.UI.Xaml.Window window, bool cloaked)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = cloaked ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaCloak, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);
}
