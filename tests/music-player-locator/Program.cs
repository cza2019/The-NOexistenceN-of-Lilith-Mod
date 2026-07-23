using System;
using System.Linq;
using System.IO;
using LilithMod;

static void Equal(string expected, string actual, string label)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'");
    }
}

var record = new MusicInstallRecord(
    "QQ音乐",
    @"D:\apps\QQMusic\QQMusic2145.04.21.20",
    @"D:\apps\QQMusic\QQMusic2145.04.21.20\QQMusic.exe");
string[] candidates = MusicPlayerLocator
    .CandidatesFromInstallRecord(record, ExternalMusicPlayer.QqMusic)
    .ToArray();
Equal(
    @"D:\apps\QQMusic\QQMusic2145.04.21.20\QQMusic.exe",
    candidates[0],
    "versioned install location");
Equal(
    @"D:\apps\QQMusic\QQMusic2145.04.21.20\QQMusic.exe",
    candidates[1],
    "display icon");
Equal(
    @"D:\apps\QQMusic\QQMusic.exe",
    MusicPlayerLocator.NormalizeDisplayIcon(@"""D:\apps\QQMusic\QQMusic.exe"",0"),
    "quoted display icon");

if (MusicPlayerLocator.CandidatesFromInstallRecord(
        new MusicInstallRecord("Unrelated Player", @"D:\apps\other", ""),
        ExternalMusicPlayer.QqMusic).Any())
{
    throw new InvalidOperationException("Unrelated uninstall records must be ignored.");
}

var neteaseRecord = new MusicInstallRecord(
    "网易云音乐",
    @"E:\Portable\CloudMusic\9.2.1",
    "");
Equal(
    @"E:\Portable\CloudMusic\9.2.1\cloudmusic.exe",
    MusicPlayerLocator.CandidatesFromInstallRecord(
        neteaseRecord,
        ExternalMusicPlayer.NeteaseCloudMusic).Single(),
    "custom Netease install location");

string invalidCachedPath = Path.Combine(Path.GetTempPath(), "missing-lilith-player", "QQMusic.exe");
string? located = MusicPlayerLocator.Locate(ExternalMusicPlayer.QqMusic, invalidCachedPath);
if (string.Equals(located, invalidCachedPath, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("An invalid cached executable path must be rediscovered.");
}
if (located is not null && !File.Exists(located))
{
    throw new InvalidOperationException("A discovered player path must exist.");
}

Console.WriteLine("Music player locator tests passed.");
