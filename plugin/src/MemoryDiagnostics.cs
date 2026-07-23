using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Profiling;

namespace LilithMod;

internal sealed class MemoryDiagnostics
{
    private readonly bool _enabled = File.Exists(
        Path.Combine(Paths.PluginPath, "LilithMod.memory-diagnostics"));
    private float _nextSample;

    public bool Enabled => _enabled;

    public void Tick(MediaPollCoordinator media)
    {
        if (!_enabled || Time.unscaledTime < _nextSample)
        {
            return;
        }
        _nextSample = Time.unscaledTime + 60f;

        try
        {
            using Process process = Process.GetCurrentProcess();
            long managed = GC.GetTotalMemory(false);
            long unityAllocated = Profiler.GetTotalAllocatedMemoryLong();
            long unityReserved = Profiler.GetTotalReservedMemoryLong();
            LilithModPlugin.PluginLog.LogInfo(
                "Memory diagnostic: " +
                $"private={ToMb(process.PrivateMemorySize64):0.0}MB; " +
                $"working={ToMb(process.WorkingSet64):0.0}MB; " +
                $"managed={ToMb(managed):0.0}MB; " +
                $"unityAllocated={ToMb(unityAllocated):0.0}MB; " +
                $"unityReserved={ToMb(unityReserved):0.0}MB; " +
                $"handles={process.HandleCount}; threads={process.Threads.Count}; " +
                $"mediaInFlight={media.CurrentConcurrent}; mediaMax={media.MaxConcurrent}; " +
                $"mediaBegun={media.Begun}; mediaCompleted={media.Completed}");
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning(
                $"Memory diagnostic failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private static double ToMb(long bytes)
    {
        return bytes / 1024d / 1024d;
    }
}
