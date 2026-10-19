using System.Runtime.InteropServices;

namespace WorkChatMini;

public partial class MainWindow
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute", ExactSpelling = true)]
    private static extern int DwmSetChromeAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Windows 11 DWM dark caption / rounded corners; Windows 10 uses native defaults.
    // Failure is cosmetic and never blocks app startup.
    private void TryUseModernWindowChrome()
    {
        if (_windowHandle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;
        try
        {
            int dark = 1;
            _ = DwmSetChromeAttribute(_windowHandle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int corner = DwmCornerRound;
                _ = DwmSetChromeAttribute(_windowHandle, DwmWindowCornerPreference, ref corner, sizeof(int));
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
