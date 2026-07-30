using System;
using System.Runtime.Versioning;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace LilithMod;

[SupportedOSPlatform("windows")]
internal static class TrayIntegration
{
    private static readonly IExternalMusicLauncher MusicLauncher = new WindowsExternalMusicLauncher();
    private static readonly ISettingsLauncher ModSettingsLauncher = SettingsLauncher.Create();
    private static LilithRuntimeBehaviour? _runtime;
    private static Il2CppSystem.Action? _showChat;
    private static Il2CppSystem.Action? _openConfiguration;
    private static bool _initialized;
    private static bool _failureLogged;
    private static IntPtr _observedTrayIdentity;
    private static long _trayReadySinceMs;
    private static int _registeredActionCount = -1;

    private const long TrayReadyDelayMs = 2500;

    public static void Bind(LilithRuntimeBehaviour runtime)
    {
        _runtime = runtime;
    }

    public static void Unbind(LilithRuntimeBehaviour runtime)
    {
        if (ReferenceEquals(_runtime, runtime))
        {
            _runtime = null;
        }
    }

    public static void TryInitialize()
    {
        if (_runtime is null)
        {
            return;
        }

        try
        {
            ShowSystemTray? host = ShowSystemTray.instance;
            ISystemTray? tray = host?.tray;
            if (host is null || !host.initialized || tray is null)
            {
                return;
            }

            long now = Environment.TickCount64;
            TraySnapshot snapshot = InspectTray(tray);
            if (snapshot.Identity != _observedTrayIdentity)
            {
                if (_observedTrayIdentity != IntPtr.Zero)
                {
                    LilithModPlugin.PluginLog.LogInfo(
                        "Native system tray instance changed; Mod entries will be registered again");
                }
                _observedTrayIdentity = snapshot.Identity;
                _trayReadySinceMs = now;
                _registeredActionCount = -1;
                _initialized = false;
            }

            if (_initialized)
            {
                if (_registeredActionCount >= 0
                    && snapshot.ActionCount >= 0
                    && snapshot.ActionCount < _registeredActionCount)
                {
                    LilithModPlugin.PluginLog.LogInfo(
                        $"Native tray menu was rebuilt; actions={snapshot.ActionCount}, " +
                        $"expectedAtLeast={_registeredActionCount}");
                    _trayReadySinceMs = now;
                    _registeredActionCount = -1;
                    _initialized = false;
                }
                else
                {
                    return;
                }
            }

            if (now - _trayReadySinceMs < TrayReadyDelayMs)
            {
                return;
            }

            _showChat ??= (System.Action)(() => _runtime?.ShowChatWindow());
            _openConfiguration ??= (System.Action)OpenApiConfiguration;

            tray.AddSeparator();
        tray.AddItem(ModLocalization.T("menu.chat"), _showChat);
        tray.AddItem(ModLocalization.T("menu.mod_settings"), _openConfiguration);
            TraySnapshot registered = InspectTray(tray);
            _observedTrayIdentity = registered.Identity;
            _registeredActionCount = registered.ActionCount;
            _initialized = true;
            LilithModPlugin.PluginLog.LogInfo(
                $"Lilith Mod tray entries registered; actions={_registeredActionCount}");
        }
        catch (Exception error)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                LilithModPlugin.PluginLog.LogWarning(
                    $"Tray integration failed: {error.GetType().Name}: {error.Message}");
            }
        }
    }

    private static TraySnapshot InspectTray(ISystemTray tray)
    {
        try
        {
            if (tray is not Il2CppObjectBase trayObject)
            {
                return new TraySnapshot(IntPtr.Zero, -1);
            }

            WindowsSystemTrayAdapter? adapter = trayObject.TryCast<WindowsSystemTrayAdapter>();
            SystemTray? nativeTray = adapter?._tray;
            if (nativeTray is null)
            {
                return new TraySnapshot(trayObject.Pointer, -1);
            }

            int actionCount = nativeTray._menuActions?.Count ?? -1;
            return new TraySnapshot(nativeTray.Pointer, actionCount);
        }
        catch
        {
            return new TraySnapshot(IntPtr.Zero, -1);
        }
    }

    private readonly record struct TraySnapshot(IntPtr Identity, int ActionCount);

    public static void OpenPlayer(ExternalMusicPlayer player, string displayName)
    {
        ExternalMusicState.SetArmed(true);
        bool opened = MusicLauncher.Open(player);
        if (!opened)
        {
        Notify(ModLocalization.T("menu.player_missing", displayName));
        }
    }

    public static void OpenPreferredPlayer()
    {
        ExternalMusicPlayer preferred = MusicLauncher.PreferredPlayer();
        string preferredName = PlayerName(preferred);

        ExternalMusicState.SetArmed(true);
        foreach (ExternalMusicPlayer player in MusicPlayerLocator.OpenOrder(preferred))
        {
            if (!MusicLauncher.Open(player))
            {
                continue;
            }
            if (player != preferred)
            {
                LilithModPlugin.PluginLog.LogInfo(
                    $"Preferred music player unavailable; opened {PlayerName(player)} instead of {preferredName}");
            }
            return;
        }
        Notify(ModLocalization.T("menu.players_missing"));
    }

    private static string PlayerName(ExternalMusicPlayer player)
    {
        return player switch
        {
            ExternalMusicPlayer.QqMusic => ModLocalization.T("menu.qq_music"),
            ExternalMusicPlayer.NeteaseCloudMusic => ModLocalization.T("menu.netease_music"),
            ExternalMusicPlayer.Spotify => ModLocalization.T("menu.spotify"),
            ExternalMusicPlayer.AppleMusic => ModLocalization.T("menu.apple_music"),
            ExternalMusicPlayer.AmazonMusic => ModLocalization.T("menu.amazon_music"),
            _ => ModLocalization.T("menu.qq_music")
        };
    }

    public static void ShowChat()
    {
        _runtime?.ShowChatWindow();
    }

    public static void BeginChatFromLilithLine(string line)
    {
        _runtime?.BeginChatFromLilithLine(line);
    }

    public static bool IsChatPointerOver()
    {
        return _runtime?.IsChatPointerOver() == true;
    }

    public static void OpenApiConfiguration()
    {
        LilithModPlugin.PluginLog.LogInfo("API configuration requested");
        if (ModSettingsLauncher.Open(out string error))
        {
            return;
        }
        LilithModPlugin.PluginLog.LogWarning(error);
        Notify(error);
    }

    public static void Notify(string message)
    {
        _runtime?.NotifyUser(message);
        try
        {
            ShowSystemTray.instance?.tray?.ShowNotification(ModLocalization.T("common.lilith"), message, 3500);
        }
        catch
        {
            // The native chat history still receives the message.
        }
    }
}
