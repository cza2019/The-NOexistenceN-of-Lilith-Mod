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
            tray.AddItem("和莉莉丝聊天", _showChat);
            tray.AddItem("Mod 设置", _openConfiguration);
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
            Notify($"没有找到{displayName}，请先安装或手动启动播放器。");
        }
    }

    public static void OpenPreferredPlayer()
    {
        ExternalMusicPlayer preferred = MusicLauncher.PreferredPlayer();
        ExternalMusicPlayer fallback = preferred == ExternalMusicPlayer.QqMusic
            ? ExternalMusicPlayer.NeteaseCloudMusic
            : ExternalMusicPlayer.QqMusic;
        string preferredName = PlayerName(preferred);
        string fallbackName = PlayerName(fallback);

        ExternalMusicState.SetArmed(true);
        if (MusicLauncher.Open(preferred))
        {
            return;
        }
        if (MusicLauncher.Open(fallback))
        {
            LilithModPlugin.PluginLog.LogInfo(
                $"Preferred music player unavailable; opened {fallbackName} instead of {preferredName}");
            return;
        }
        Notify("没有找到 QQ 音乐或网易云音乐，请先安装一个音乐播放器。");
    }

    private static string PlayerName(ExternalMusicPlayer player)
    {
        return player == ExternalMusicPlayer.QqMusic ? "QQ 音乐" : "网易云音乐";
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
            ShowSystemTray.instance?.tray?.ShowNotification("莉莉丝", message, 3500);
        }
        catch
        {
            // The native chat history still receives the message.
        }
    }
}
