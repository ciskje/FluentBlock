using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluentBlock.Services;

/// <summary>Applies Mica backdrop on Windows 11, falls back silently.</summary>
public static class ThemeService
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    /// <summary>Paints AccentBrush with the Windows system color (DWM); keeps the default on failure.</summary>
    public static void ApplySystemAccent()
    {
        try
        {
            if (DwmGetColorizationColor(out uint c, out _) != 0) return;
            if (Application.Current?.Resources["AccentBrush"] is System.Windows.Media.SolidColorBrush brush)
                brush.Color = System.Windows.Media.Color.FromArgb(
                    (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
        }
        catch { /* keep default accent */ }
    }

    public static void ApplyMica(Window window)
    {
        try
        {
            window.SourceInitialized += (_, _) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero) return;
                    // Only on Win11 22000+. DwmSetWindowAttribute fails silently otherwise.
                    int type = DWMSBT_MAINWINDOW;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
                }
                catch { /* fallback: solid background */ }
            };
        }
        catch { }
    }

    public static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
