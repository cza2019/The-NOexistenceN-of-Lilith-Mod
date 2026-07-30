using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LilithMod;

internal enum ExternalMusicPlayer
{
    QqMusic,
    NeteaseCloudMusic,
    Spotify,
    AppleMusic,
    AmazonMusic
}

internal enum MusicLaunchKind
{
    Executable,
    AppUserModelId
}

internal sealed record MusicLaunchTarget(MusicLaunchKind Kind, string Value);

internal sealed record MusicInstallRecord(string DisplayName, string InstallLocation, string DisplayIcon);

internal static class MusicPlayerLocator
{
    private static readonly ExternalMusicPlayer[] PlayerOrder =
    {
        ExternalMusicPlayer.QqMusic,
        ExternalMusicPlayer.NeteaseCloudMusic,
        ExternalMusicPlayer.Spotify,
        ExternalMusicPlayer.AppleMusic,
        ExternalMusicPlayer.AmazonMusic
    };

    internal static string ConfigKey(ExternalMusicPlayer player) => player switch
    {
        ExternalMusicPlayer.QqMusic => "qqmusic",
        ExternalMusicPlayer.NeteaseCloudMusic => "netease",
        ExternalMusicPlayer.Spotify => "spotify",
        ExternalMusicPlayer.AppleMusic => "applemusic",
        ExternalMusicPlayer.AmazonMusic => "amazonmusic",
        _ => "qqmusic"
    };

    internal static bool TryParseConfigKey(string? value, out ExternalMusicPlayer player)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "netease":
                player = ExternalMusicPlayer.NeteaseCloudMusic;
                return true;
            case "spotify":
                player = ExternalMusicPlayer.Spotify;
                return true;
            case "applemusic":
            case "apple_music":
                player = ExternalMusicPlayer.AppleMusic;
                return true;
            case "amazonmusic":
            case "amazon_music":
                player = ExternalMusicPlayer.AmazonMusic;
                return true;
            case "qqmusic":
                player = ExternalMusicPlayer.QqMusic;
                return true;
            default:
                player = ExternalMusicPlayer.QqMusic;
                return false;
        }
    }

    internal static IEnumerable<ExternalMusicPlayer> OpenOrder(ExternalMusicPlayer preferred)
    {
        yield return preferred;
        foreach (ExternalMusicPlayer player in PlayerOrder)
        {
            if (player != preferred)
            {
                yield return player;
            }
        }
    }

    internal static string ExecutableName(ExternalMusicPlayer player) => player switch
    {
        ExternalMusicPlayer.QqMusic => "QQMusic.exe",
        ExternalMusicPlayer.NeteaseCloudMusic => "cloudmusic.exe",
        ExternalMusicPlayer.Spotify => "Spotify.exe",
        ExternalMusicPlayer.AppleMusic => "AppleMusic.exe",
        ExternalMusicPlayer.AmazonMusic => "Amazon Music.exe",
        _ => "QQMusic.exe"
    };

    internal static IReadOnlyList<string> ProcessNames(ExternalMusicPlayer player) => player switch
    {
        ExternalMusicPlayer.QqMusic => new[] { "QQMusic" },
        ExternalMusicPlayer.NeteaseCloudMusic => new[] { "cloudmusic" },
        ExternalMusicPlayer.Spotify => new[] { "Spotify" },
        ExternalMusicPlayer.AppleMusic => new[] { "AppleMusic", "Music" },
        ExternalMusicPlayer.AmazonMusic => new[] { "Amazon Music", "AmazonMusic" },
        _ => Array.Empty<string>()
    };

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

    internal static IEnumerable<MusicLaunchTarget> LocateLaunchTargets(
        ExternalMusicPlayer player,
        string? cachedPath = null)
    {
        string? executable = Locate(player, cachedPath);
        if (!string.IsNullOrWhiteSpace(executable))
        {
            yield return new MusicLaunchTarget(MusicLaunchKind.Executable, executable);
        }

        foreach (string appUserModelId in StoreAppUserModelIds(player))
        {
            yield return new MusicLaunchTarget(MusicLaunchKind.AppUserModelId, appUserModelId);
        }
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
        return player switch
        {
            ExternalMusicPlayer.QqMusic =>
                name.Contains("QQMusic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("QQ Music", StringComparison.OrdinalIgnoreCase)
                || name.Contains("QQ\u97f3\u4e50", StringComparison.OrdinalIgnoreCase),
            ExternalMusicPlayer.NeteaseCloudMusic =>
                name.Contains("CloudMusic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("NetEase", StringComparison.OrdinalIgnoreCase)
                || name.Contains("\u7f51\u6613\u4e91\u97f3\u4e50", StringComparison.OrdinalIgnoreCase),
            ExternalMusicPlayer.Spotify => name.Contains("Spotify", StringComparison.OrdinalIgnoreCase),
            ExternalMusicPlayer.AppleMusic =>
                name.Contains("Apple Music", StringComparison.OrdinalIgnoreCase)
                || name.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase),
            ExternalMusicPlayer.AmazonMusic =>
                name.Contains("Amazon Music", StringComparison.OrdinalIgnoreCase)
                || name.Contains("AmazonMusic", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsExecutable(string? path, string executableName)
    {
        return !string.IsNullOrWhiteSpace(path)
            && string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(path);
    }

    private static IEnumerable<string> StoreAppUserModelIds(ExternalMusicPlayer player)
    {
        switch (player)
        {
            case ExternalMusicPlayer.Spotify:
                yield return "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
                break;
            case ExternalMusicPlayer.AppleMusic:
                yield return "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";
                break;
            case ExternalMusicPlayer.AmazonMusic:
                yield return "AmazonMobileLLC.AmazonMusic_kc6t79cpj4tp0!AmazonMobileLLC.AmazonMusic";
                break;
        }
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
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        switch (player)
        {
            case ExternalMusicPlayer.QqMusic:
                yield return Path.Combine(local, "Tencent", "QQMusic", executableName);
                yield return Path.Combine(programFiles, "Tencent", "QQMusic", executableName);
                yield return Path.Combine(programFilesX86, "Tencent", "QQMusic", executableName);
                break;
            case ExternalMusicPlayer.NeteaseCloudMusic:
                yield return Path.Combine(local, "Netease", "CloudMusic", executableName);
                yield return Path.Combine(programFiles, "Netease", "CloudMusic", executableName);
                yield return Path.Combine(programFilesX86, "Netease", "CloudMusic", executableName);
                break;
            case ExternalMusicPlayer.Spotify:
                yield return Path.Combine(roaming, "Spotify", executableName);
                yield return Path.Combine(local, "Spotify", executableName);
                yield return Path.Combine(programFiles, "Spotify", executableName);
                yield return Path.Combine(programFilesX86, "Spotify", executableName);
                break;
            case ExternalMusicPlayer.AppleMusic:
                yield return Path.Combine(local, "Apple", "Apple Music", executableName);
                yield return Path.Combine(programFiles, "Apple", "Apple Music", executableName);
                yield return Path.Combine(programFilesX86, "Apple", "Apple Music", executableName);
                break;
            case ExternalMusicPlayer.AmazonMusic:
                yield return Path.Combine(local, "Amazon Music", executableName);
                yield return Path.Combine(programFiles, "Amazon Music", executableName);
                yield return Path.Combine(programFilesX86, "Amazon Music", executableName);
                break;
        }
    }
}
