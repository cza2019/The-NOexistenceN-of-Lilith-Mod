using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LilithMod;

[SupportedOSPlatform("windows")]
internal static class StartupUpdatePrompt
{
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconInformation = 0x00000040;
    private const uint MbDefButton2 = 0x00000100;
    private const uint MbTaskModal = 0x00002000;
    private const uint MbSetForeground = 0x00010000;
    private const int IdYes = 6;

    internal static void Show(string latestVersion, string releaseUrl)
    {
        if (!OperatingSystem.IsWindows()
            || !UpdateReleaseUrl.TryParse(releaseUrl, out Uri? trustedRelease)
            || trustedRelease is null)
        {
            LilithModPlugin.PluginLog.LogWarning(
                "Startup update prompt ignored an invalid GitHub Release URL");
            return;
        }

        try
        {
            IntPtr owner = Process.GetCurrentProcess().MainWindowHandle;
            int choice = MessageBoxW(
                owner,
                ModLocalization.T("settings.update_new_prompt", latestVersion),
                ModLocalization.T("settings.update_new_title"),
                MbYesNo | MbIconInformation | MbDefButton2 | MbTaskModal | MbSetForeground);
            Uri? selected = UpdateReleaseUrl.Select(choice == IdYes, trustedRelease.AbsoluteUri);
            if (selected is null)
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = selected.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Could not show or open the startup update prompt: {error.GetType().Name}");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
