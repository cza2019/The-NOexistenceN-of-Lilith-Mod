using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LilithMod;

internal enum ExternalMusicPlayer
{
    QqMusic,
    NeteaseCloudMusic
}

internal sealed record MusicInstallRecord(string DisplayName, string InstallLocation, string DisplayIcon);

internal static class MusicPlayerLocator
{
    internal static string ExecutableName(ExternalMusicPlayer player) =>
        player == ExternalMusicPlayer.QqMusic ? "QQMusic.exe" : "cloudmusic.exe";

    internal static string? Locate(ExternalMusicPlayer player, string? cachedPath = null)
    {
        string executableName = ExecutableName(player);
        if (IsExecutable(cachedPath, executableName))
        {
            return Path.GetFullPath(cachedPath!);
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (string candidate in RegistryAppPaths(executableName))
            {
                if (IsExecutable(candidate, executableName))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            foreach (MusicInstallRecord record in RegistryInstallRecords())
            {
                foreach (string candidate in CandidatesFromInstallRecord(record, player))
                {
                    if (IsExecutable(candidate, executableName))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
        }

        foreach (string candidate in KnownPaths(player))
        {
            if (IsExecutable(candidate, executableName))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    internal static IEnumerable<string> CandidatesFromInstallRecord(
        MusicInstallRecord record,
        ExternalMusicPlayer player)
    {
        if (!MatchesPlayer(record.DisplayName, player))
        {
            yield break;
        }

        string executableName = ExecutableName(player);
        if (!string.IsNullOrWhiteSpace(record.InstallLocation))
        {
            yield return Path.Combine(record.InstallLocation.Trim().Trim('"'), executableName);
        }

        string iconPath = NormalizeDisplayIcon(record.DisplayIcon);
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            yield return iconPath;
        }
    }

    internal static string NormalizeDisplayIcon(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.StartsWith('"'))
        {
            int closingQuote = text.IndexOf('"', 1);
            return closingQuote > 1 ? text.Substring(1, closingQuote - 1) : text.Trim('"');
        }

        int iconIndex = text.LastIndexOf(',');
        if (iconIndex > 1 && int.TryParse(text[(iconIndex + 1)..].Trim(), out _))
        {
            text = text[..iconIndex];
        }
        return text.Trim().Trim('"');
    }

    private static bool MatchesPlayer(string? displayName, ExternalMusicPlayer player)
    {
        string name = displayName ?? string.Empty;
        return player == ExternalMusicPlayer.QqMusic
            ? name.Contains("QQMusic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("QQ Music", StringComparison.OrdinalIgnoreCase)
                || name.Contains("QQ音乐", StringComparison.OrdinalIgnoreCase)
            : name.Contains("CloudMusic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("NetEase", StringComparison.OrdinalIgnoreCase)
                || name.Contains("网易云音乐", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExecutable(string? path, string executableName)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(path);
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> RegistryAppPaths(string executableName)
    {
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? baseKey = null;
                RegistryKey? key = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    key = baseKey.OpenSubKey(
                        $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
                    if (key?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        yield return value.Trim().Trim('"');
                    }
                }
                finally
                {
                    key?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<MusicInstallRecord> RegistryInstallRecords()
    {
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? baseKey = null;
                RegistryKey? uninstall = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null)
                    {
                        continue;
                    }

                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        using RegistryKey? entry = uninstall.OpenSubKey(name);
                        if (entry is null)
                        {
                            continue;
                        }
                        yield return new MusicInstallRecord(
                            entry.GetValue("DisplayName") as string ?? string.Empty,
                            entry.GetValue("InstallLocation") as string ?? string.Empty,
                            entry.GetValue("DisplayIcon") as string ?? string.Empty);
                    }
                }
                finally
                {
                    uninstall?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }
    }

    private static IEnumerable<string> KnownPaths(ExternalMusicPlayer player)
    {
        string executableName = ExecutableName(player);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (player == ExternalMusicPlayer.QqMusic)
        {
            yield return Path.Combine(local, "Tencent", "QQMusic", executableName);
            yield return Path.Combine(programFiles, "Tencent", "QQMusic", executableName);
            yield return Path.Combine(programFilesX86, "Tencent", "QQMusic", executableName);
        }
        else
        {
            yield return Path.Combine(local, "Netease", "CloudMusic", executableName);
            yield return Path.Combine(programFiles, "Netease", "CloudMusic", executableName);
            yield return Path.Combine(programFilesX86, "Netease", "CloudMusic", executableName);
        }
    }
}
