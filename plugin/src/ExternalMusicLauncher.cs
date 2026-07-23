namespace LilithMod;

internal interface IExternalMusicLauncher
{
    bool Open(ExternalMusicPlayer player);

    ExternalMusicPlayer PreferredPlayer();
}

internal sealed class WindowsExternalMusicLauncher : IExternalMusicLauncher
{
    public bool Open(ExternalMusicPlayer player) => MusicAppLauncher.Open(player);

    public ExternalMusicPlayer PreferredPlayer() => MusicAppLauncher.PreferredPlayer();
}
