using System;
using System.IO;
using System.Threading;
using BepInEx;
using HarmonyLib;
using BepInEx.Unity.IL2CPP;

namespace LilithMod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class LilithModPlugin : BasePlugin
{
    public const string PluginGuid = "com.codex.lilith.mod";
    public const string PluginName = "Lilith Mod";
    public const string PluginVersion = "0.3.1";

    internal static BepInEx.Logging.ManualLogSource PluginLog { get; private set; } = null!;
    private static LilithModPlugin? _instance;

    private Harmony? _harmony;
    private CompanionProcess? _companion;

    public override void Load()
    {
        _instance = this;
        PluginLog = Log;
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        Log.LogInfo($"Process: {Environment.ProcessPath}");
        Log.LogInfo($"Runtime: {Environment.Version}");

        bool noPatches = File.Exists(Path.Combine(Paths.PluginPath, "LilithMod.no-patches"));
        bool noRuntime = File.Exists(Path.Combine(Paths.PluginPath, "LilithMod.no-runtime"));
        bool patchesOnly = File.Exists(Path.Combine(Paths.PluginPath, "LilithMod.patches-only"));
        bool fullMode = File.Exists(Path.Combine(Paths.PluginPath, "LilithMod.full"));
        if (fullMode)
        {
            noPatches = false;
            noRuntime = false;
        }
        if (patchesOnly && !fullMode)
        {
            noPatches = false;
            noRuntime = true;
        }
        if (File.Exists(Path.Combine(Paths.PluginPath, "LilithMod.probe-only")) && !noPatches && !noRuntime && !fullMode)
        {
            Log.LogWarning("Probe-only sentinel present; runtime component and patches are disabled");
            return;
        }

        if (!noPatches)
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(UserInterfacePatches));
            CrossScreenDragPatches.Apply(_harmony);
            Log.LogInfo("User interface polling registered");
        }
        if (!noRuntime)
        {
            _companion = CompanionProcess.TryStart();
            AddComponent<LilithRuntimeBehaviour>();
        }
        if (noPatches || noRuntime)
        {
            Log.LogWarning($"Diagnostic sentinel active: noPatches={noPatches}, noRuntime={noRuntime}");
        }
        Log.LogInfo($"Runtime chat component registered: {!noRuntime}; music-menu UI patch registered: {!noPatches}");
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        StopCompanion();
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
        return true;
    }

    internal static void ShutdownCompanion()
    {
        _instance?.StopCompanion();
    }

    internal static void EnsureCompanionRunning()
    {
        _instance?.EnsureCompanion();
    }

    private void EnsureCompanion()
    {
        if (_companion is not null && !_companion.HasExited)
        {
            return;
        }
        if (CompanionProcess.IsRuntimeRunning())
        {
            return;
        }
        CompanionProcess? previous = Interlocked.Exchange(ref _companion, null);
        previous?.Dispose();
        Log.LogWarning("Companion is unavailable; attempting restart");
        _companion = CompanionProcess.TryStart();
    }

    private void StopCompanion()
    {
        CompanionProcess? companion = Interlocked.Exchange(ref _companion, null);
        if (companion is null)
        {
            return;
        }
        Log.LogInfo("Stopping Companion process");
        companion.Dispose();
    }
}
