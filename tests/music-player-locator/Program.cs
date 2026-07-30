using System;
using System.IO;
using System.Linq;
using LilithMod;

static void Equal(string expected, string actual, string label)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'");
    }
}

static void True(bool value, string label)
{
    if (!value)
    {
        throw new InvalidOperationException(label);
    }
}

var record = new MusicInstallRecord(
    "QQMusic",
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

True(
    !MusicPlayerLocator.CandidatesFromInstallRecord(
        new MusicInstallRecord("Unrelated Player", @"D:\apps\other", ""),
        ExternalMusicPlayer.QqMusic).Any(),
    "unrelated uninstall records must be ignored");

var neteaseRecord = new MusicInstallRecord(
    "NetEase Cloud Music",
    @"E:\Portable\CloudMusic\9.2.1",
    "");
Equal(
    @"E:\Portable\CloudMusic\9.2.1\cloudmusic.exe",
    MusicPlayerLocator.CandidatesFromInstallRecord(
        neteaseRecord,
        ExternalMusicPlayer.NeteaseCloudMusic).Single(),
    "custom Netease install location");

var spotifyRecord = new MusicInstallRecord(
    "Spotify",
    @"D:\apps\Spotify",
    "");
Equal(
    @"D:\apps\Spotify\Spotify.exe",
    MusicPlayerLocator.CandidatesFromInstallRecord(
        spotifyRecord,
        ExternalMusicPlayer.Spotify).Single(),
    "custom Spotify install location");

foreach (var expected in new[]
{
    ("qqmusic", ExternalMusicPlayer.QqMusic),
    ("netease", ExternalMusicPlayer.NeteaseCloudMusic),
    ("spotify", ExternalMusicPlayer.Spotify),
    ("applemusic", ExternalMusicPlayer.AppleMusic),
    ("amazonmusic", ExternalMusicPlayer.AmazonMusic)
})
{
    True(
        MusicPlayerLocator.TryParseConfigKey(expected.Item1, out ExternalMusicPlayer actual)
        && actual == expected.Item2,
        $"configuration value {expected.Item1} must map to the correct player");
    Equal(expected.Item1, MusicPlayerLocator.ConfigKey(expected.Item2), "configuration round trip");
}

ExternalMusicPlayer[] fallbackOrder = MusicPlayerLocator
    .OpenOrder(ExternalMusicPlayer.AppleMusic)
    .ToArray();
True(fallbackOrder[0] == ExternalMusicPlayer.AppleMusic, "preferred player must be attempted first");
True(fallbackOrder.Distinct().Count() == 5, "fallback order must include every player exactly once");
True(
    fallbackOrder.SequenceEqual(new[]
    {
        ExternalMusicPlayer.AppleMusic,
        ExternalMusicPlayer.QqMusic,
        ExternalMusicPlayer.NeteaseCloudMusic,
        ExternalMusicPlayer.Spotify,
        ExternalMusicPlayer.AmazonMusic
    }),
    "fallback order must remain deterministic");

True(
    MusicPlayerLocator.LocateLaunchTargets(ExternalMusicPlayer.Spotify)
        .Any(target => target.Kind == MusicLaunchKind.AppUserModelId
            && target.Value == "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"),
    "Spotify Store app identifier must be available as a launch target");
True(
    MusicPlayerLocator.LocateLaunchTargets(ExternalMusicPlayer.AppleMusic)
        .Any(target => target.Kind == MusicLaunchKind.AppUserModelId
            && target.Value == "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"),
    "Apple Music Store app identifier must be available as a launch target");
True(
    MusicPlayerLocator.LocateLaunchTargets(ExternalMusicPlayer.AmazonMusic)
        .Any(target => target.Kind == MusicLaunchKind.AppUserModelId
            && target.Value == "AmazonMobileLLC.AmazonMusic_kc6t79cpj4tp0!AmazonMobileLLC.AmazonMusic"),
    "Amazon Music Store app identifier must be available as a launch target");

string invalidCachedPath = Path.Combine(Path.GetTempPath(), "missing-lilith-player", "QQMusic.exe");
string? located = MusicPlayerLocator.Locate(ExternalMusicPlayer.QqMusic, invalidCachedPath);
True(
    !string.Equals(located, invalidCachedPath, StringComparison.OrdinalIgnoreCase),
    "an invalid cached executable path must be rediscovered");
True(located is null || File.Exists(located), "a discovered player path must exist");

Console.WriteLine("Music player locator tests passed.");
