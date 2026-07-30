using System;
using HarmonyLib;

namespace LilithMod;

internal static class ExternalMusicState
{
    private static volatile bool _playing;
    private static volatile bool _armed;
    private static volatile bool _behaviorConsumed;

    public static bool Armed => _armed;
    public static bool Playing => _armed && _playing;
    public static bool BehaviorConsumed => _armed && _playing && _behaviorConsumed;

    public static void SetArmed(bool value)
    {
        _armed = value;
        if (!value)
        {
            _playing = false;
            _behaviorConsumed = false;
        }
    }

    public static void Update(ExternalMediaState state)
    {
        _playing = _armed && state.Available && state.Playing && IsMusicSource(state.Source);
        if (!_playing)
        {
            _behaviorConsumed = false;
        }
    }

    public static void MarkBehaviorConsumed(bool value) => _behaviorConsumed = value;

    private static bool IsMusicSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        string value = source.ToLowerInvariant();
        if (value.Contains("chrome") || value.Contains("msedge") || value.Contains("firefox") || value.Contains("browser"))
        {
            return false;
        }

        return value.Contains("cloudmusic")
            || value.Contains("netease")
            || value.Contains("qqmusic")
            || value.Contains("spotify")
            || value.Contains("music.ui")
            || value.Contains("applemusic")
            || value.Contains("appleinc.applemusicwin")
            || value.Contains("amazonmusic")
            || value.Contains("amazon music")
            || value.Contains("amazonmobilellc.amazonmusic");
    }
}

internal static class MusicBehaviorScope
{
    [ThreadStatic]
    private static int _depth;

    public static bool Active => _depth > 0;
    public static void Enter() => _depth++;
    public static void Exit() => _depth = Math.Max(0, _depth - 1);
}

internal static class MusicBehaviorEligibility
{
    public static bool IsAllowed()
    {
        try
        {
            CharacterController? controller = CharacterController.s_activeInstance;
            return controller is not null
                && controller.IsIdle
                && !controller.IsSleep
                && !controller.IsDialogueActive
                && !controller.IsInteracting
                && !controller.IsStateMachineLocked;
        }
        catch
        {
            // A partially initialized character should keep the original game
            // behavior rather than being forced into a music state.
            return false;
        }
    }
}

internal static class ExternalMusicPatches
{
    [HarmonyPatch(typeof(AudioManager), "get_IsUserMusicPlaying")]
    private static class UserMusicPlayingPatch
    {
        private static bool _loggedInvocation;
        private static bool _loggedConsumption;

        private static void Postfix(ref bool __result)
        {
            if (!_loggedInvocation)
            {
                _loggedInvocation = true;
                LilithModPlugin.PluginLog.LogInfo("AudioManager.IsUserMusicPlaying invoked");
            }
            if (!__result && ExternalMusicState.Playing && MusicBehaviorEligibility.IsAllowed())
            {
                __result = true;
                ExternalMusicState.MarkBehaviorConsumed(true);
                if (!_loggedConsumption)
                {
                    _loggedConsumption = true;
                    LilithModPlugin.PluginLog.LogInfo(
                        "MusicListeningBehavior consumed the external playing state");
                }
            }
            else if (!ExternalMusicState.Playing)
            {
                _loggedConsumption = false;
            }
        }
    }

    [HarmonyPatch(typeof(MusicListeningBehavior), nameof(MusicListeningBehavior.CanRun))]
    private static class CanRunPatch
    {
        private static bool _loggedInvocation;
        private static bool _loggedCanRun;

        private static void Postfix(ref bool __result)
        {
            if (!_loggedInvocation)
            {
                _loggedInvocation = true;
                LilithModPlugin.PluginLog.LogInfo("MusicListeningBehavior.CanRun invoked");
            }
            if (ExternalMusicState.Playing && MusicBehaviorEligibility.IsAllowed())
            {
                if (!__result)
                {
                    __result = true;
                }
                ExternalMusicState.MarkBehaviorConsumed(true);
                if (!_loggedCanRun)
                {
                    _loggedCanRun = true;
                    LilithModPlugin.PluginLog.LogInfo(
                        "MusicListeningBehavior.CanRun forced true for external music");
                }
            }
            else
            {
                _loggedCanRun = false;
            }
        }
    }

    [HarmonyPatch(typeof(MusicListeningBehavior), nameof(MusicListeningBehavior.CanRun))]
    private static class CanRunScopePatch
    {
        private static void Prefix() => MusicBehaviorScope.Enter();
        private static Exception? Finalizer(Exception? __exception)
        {
            MusicBehaviorScope.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MusicListeningBehavior), nameof(MusicListeningBehavior.Begin))]
    private static class BeginScopePatch
    {
        private static bool _loggedBegin;

        private static void Prefix()
        {
            MusicBehaviorScope.Enter();
            if (!_loggedBegin)
            {
                _loggedBegin = true;
                LilithModPlugin.PluginLog.LogInfo("MusicListeningBehavior.Begin invoked");
            }
        }
        private static Exception? Finalizer(Exception? __exception)
        {
            MusicBehaviorScope.Exit();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MusicListeningBehavior), nameof(MusicListeningBehavior.Tick))]
    private static class TickScopePatch
    {
        private static bool _loggedTick;

        private static void Prefix()
        {
            MusicBehaviorScope.Enter();
            if (!_loggedTick)
            {
                _loggedTick = true;
                LilithModPlugin.PluginLog.LogInfo("MusicListeningBehavior.Tick invoked");
            }
        }
        private static Exception? Finalizer(Exception? __exception)
        {
            MusicBehaviorScope.Exit();
            return __exception;
        }
    }
}
