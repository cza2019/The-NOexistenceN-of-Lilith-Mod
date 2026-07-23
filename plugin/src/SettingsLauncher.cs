using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace LilithMod;

internal interface ISettingsLauncher
{
    bool Open(out string error);
}

internal static class SettingsLauncher
{
    public static ISettingsLauncher Create()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsSettingsLauncher()
            : new UnsupportedSettingsLauncher();
    }

    private sealed class UnsupportedSettingsLauncher : ISettingsLauncher
    {
        public bool Open(out string error)
        {
            error = "当前平台还没有可用的 Mod 设置窗口。";
            return false;
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsSettingsLauncher : ISettingsLauncher
{
    public bool Open(out string error)
    {
        string pluginRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        string script = Path.Combine(pluginRoot, "companion", "configure.ps1");
        if (!File.Exists(script))
        {
            error = "找不到 API 配置脚本，请确认 companion 文件夹完整。\n" + script;
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = Path.GetDirectoryName(script) ?? pluginRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-STA",
                    "-WindowStyle",
                    "Hidden",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    script
                }
            });
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = $"无法打开 Mod 设置窗口：{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
