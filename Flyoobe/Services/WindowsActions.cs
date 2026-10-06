using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Flyoobe3.Services;

//collects the small jobs Flyoobe deliberately hands back to Windows
//that includes native Settings pages, the local-account wizard, admin checks and shell refresh messages
internal static class WindowsActions
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void OpenWindowsUpdate() => Open("ms-settings:windowsupdate-action");
    public static void OpenAbout() => Open("ms-settings:about");
    public static void OpenNetwork() => Open("ms-settings:network-status");
    public static void OpenAccounts() => Open("ms-settings:emailandaccounts");
    public static void OpenOtherUsers() => Open("ms-settings:otherusers");
    public static void OpenPersonalization() => Open("ms-settings:personalization");

    //asks Explorer and open apps to pick up theme and taskbar registry changes
    public static void NotifyShellSettingsChanged()
    {
        const int HwndBroadcast = 0xffff;
        const uint WmSettingChange = 0x001A;
        const uint SmtoAbortIfHung = 0x0002;
        SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, IntPtr.Zero,
            "ImmersiveColorSet", SmtoAbortIfHung, 100, out _);
    }

    public static void RestartExplorer()
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("explorer")
                         .Where(process => process.SessionId == session))
            {
                process.Kill();
                process.WaitForExit(3000);
                process.Dispose();
            }
            Thread.Sleep(400);
            var running = Process.GetProcessesByName("explorer");
            var shellReturned = running.Any(process => process.SessionId == session);
            foreach (var process in running) process.Dispose();
            if (!shellReturned)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch { }
    }

    public static void OpenDefaultApps(string? registeredApp = null, bool userRegistration = false) =>
        Open(string.IsNullOrWhiteSpace(registeredApp)
            ? "ms-settings:defaultapps"
            : "ms-settings:defaultapps?" +
              (userRegistration ? "registeredAppUser=" : "registeredAppMachine=") +
              Uri.EscapeDataString(registeredApp));

    public static void OpenLocalAccountWizard()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "ms-cxh:localonly") { UseShellExecute = true });
    }

    private static void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam,
        string lParam, uint flags, uint timeout, out IntPtr result);
}
