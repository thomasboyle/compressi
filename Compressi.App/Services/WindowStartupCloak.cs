using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Compressi_App.Services;

/// <summary>
/// Startup visibility helpers. DWM cloak avoids the default white HWND flash
/// (microsoft-ui-xaml#7892 / #10259). A cream class brush is a belt-and-suspenders
/// fallback for any frame that still briefly shows the HWND before XAML presents.
/// </summary>
internal static class WindowStartupCloak
{
    private const int DwmwaCloak = 13;
    private const int GclpHbrBackground = -10;

    // Paper surface #E8DFD0 as COLORREF (0x00BBGGRR).
    private static readonly IntPtr PaperBrush = CreateSolidBrush(0x00D0DFE8);

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

    public static void ApplyPaperBackground(Microsoft.UI.Xaml.Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero || PaperBrush == IntPtr.Zero)
        {
            return;
        }

        _ = SetClassLongPtr(hwnd, GclpHbrBackground, PaperBrush);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int colorRef);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr newLong);
}
