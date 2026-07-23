using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LilithMod;

[SupportedOSPlatform("windows")]
internal static class MusicAppLauncher
{
    private const int SwRestore = 9;

    public static bool Open(ExternalMusicPlayer player)
    {
        string processName = player == ExternalMusicPlayer.QqMusic ? "QQMusic" : "cloudmusic";
        Process? running = Process.GetProcessesByName(processName).FirstOrDefault();
        if (running is not null)
        {
            BringToFront(running);
            return true;
        }

        string? path = MusicPlayerLocator.Locate(player, ReadCachedPath(player));
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string launchMode = StartOutsideSteamJob(path);
            if (launchMode.Length == 0)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                    UseShellExecute = true
                });
                launchMode = "direct-fallback";
            }
            WriteCachedPath(player, path);
            LilithModPlugin.PluginLog.LogInfo(
                $"Started {player}; launchMode={launchMode}");
            return true;
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Could not start {player}: {error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    private static string StartOutsideSteamJob(string path)
    {
        if (StartThroughCim(path))
        {
            return "cim-detached";
        }
        return StartThroughWindowsShell(path) ? "shell-fallback" : string.Empty;
    }

    private static bool StartThroughCim(string path)
    {
        try
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell))
            {
                return false;
            }

            string commandLine = $"\"{path}\"";
            string workingDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            string script =
                "$ErrorActionPreference='Stop';" +
                "$r=Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{" +
                $"CommandLine={PowerShellLiteral(commandLine)};" +
                $"CurrentDirectory={PowerShellLiteral(workingDirectory)}}};" +
                "if($null -eq $r -or [int]$r.ReturnValue -ne 0){exit 1};exit 0";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using Process? helper = Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}"
            });
            if (helper is null || !helper.WaitForExit(5000))
            {
                try
                {
                    helper?.Kill(true);
                }
                catch
                {
                    // A timed-out helper may already be exiting.
                }
                return false;
            }
            return helper.ExitCode == 0;
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"CIM music launch was unavailable: {error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    private static string PowerShellLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static bool StartThroughWindowsShell(string path)
    {
        object? shell = null;
        try
        {
            // Shell.Application is hosted by the Windows shell rather than the
            // Steam game process. A player cold-started here does not inherit
            // the game's process job and therefore survives Lilith.exe exiting.
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }
            shellType.InvokeMember(
                "ShellExecute",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[]
                {
                    path,
                    string.Empty,
                    Path.GetDirectoryName(path) ?? string.Empty,
                    "open",
                    1
                });
            return true;
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Detached music launch was unavailable: {error.GetType().Name}: {error.Message}");
            return false;
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    public static ExternalMusicPlayer PreferredPlayer()
    {
        try
        {
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LilithAI",
                "config.json");
            if (!File.Exists(configPath))
            {
                return ExternalMusicPlayer.QqMusic;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("music", out JsonElement music)
                && music.TryGetProperty("preferred_player", out JsonElement preferred)
                && string.Equals(preferred.GetString(), "netease", StringComparison.OrdinalIgnoreCase))
            {
                return ExternalMusicPlayer.NeteaseCloudMusic;
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Could not read music preference: {error.GetType().Name}: {error.Message}");
        }
        return ExternalMusicPlayer.QqMusic;
    }

    private static string ConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LilithAI",
            "config.json");
    }

    private static string ReadCachedPath(ExternalMusicPlayer player)
    {
        try
        {
            string configPath = ConfigPath();
            if (!File.Exists(configPath))
            {
                return string.Empty;
            }
            JsonNode? root = JsonNode.Parse(File.ReadAllText(configPath));
            string key = player == ExternalMusicPlayer.QqMusic ? "qqmusic" : "netease";
            return root?["music"]?["player_paths"]?[key]?.GetValue<string>() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void WriteCachedPath(ExternalMusicPlayer player, string path)
    {
        try
        {
            string configPath = ConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            JsonObject root = File.Exists(configPath)
                ? JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
            JsonObject music = root["music"] as JsonObject ?? new JsonObject();
            JsonObject paths = music["player_paths"] as JsonObject ?? new JsonObject();
            string key = player == ExternalMusicPlayer.QqMusic ? "qqmusic" : "netease";
            paths[key] = path;
            music["player_paths"] = paths;
            root["music"] = music;
            File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Could not cache music player path: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void BringToFront(Process process)
    {
        try
        {
            IntPtr window = process.MainWindowHandle;
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, SwRestore);
                SetForegroundWindow(window);
            }
        }
        catch
        {
            // The player is already running; foreground activation is optional.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
