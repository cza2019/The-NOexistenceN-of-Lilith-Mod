using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace LilithMod;

internal sealed class CompanionProcess : IDisposable
{
    private readonly Process _process;

    private CompanionProcess(Process process)
    {
        _process = process;
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch
            {
                return true;
            }
        }
    }

    public static CompanionProcess? TryStart()
    {
        try
        {
            if (IsRuntimeRunning())
            {
                LilithModPlugin.PluginLog.LogInfo("Using an existing Companion process");
                return null;
            }

            string pluginRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string companionRoot = Path.Combine(pluginRoot, "companion");
            string script = Path.Combine(companionRoot, "src", "server.js");
            if (!File.Exists(script))
            {
                LilithModPlugin.PluginLog.LogWarning($"Companion script not found: {script}");
                return null;
            }

            string nodeExecutable = ResolveNodeExecutable(companionRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = nodeExecutable,
                WorkingDirectory = companionRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(script);
            startInfo.Environment["LILITH_CHARACTER_FILE"] = Path.Combine(pluginRoot, "character", "lilith.json");
            startInfo.Environment["LILITH_LORE_FILE"] = Path.Combine(pluginRoot, "character", "lore", "index.json");
            startInfo.Environment["LILITH_GAME_PID"] = Environment.ProcessId.ToString();
            Process? process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("Process.Start returned null");
            }
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    LilithModPlugin.PluginLog.LogInfo($"Companion: {eventArgs.Data}");
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    LilithModPlugin.PluginLog.LogWarning($"Companion: {eventArgs.Data}");
                }
            };
            int processId = process.Id;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                try
                {
                    LilithModPlugin.PluginLog.LogWarning(
                        $"Companion process {processId} exited with code {process.ExitCode}");
                }
                catch
                {
                    LilithModPlugin.PluginLog.LogWarning($"Companion process {processId} exited");
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            LilithModPlugin.PluginLog.LogInfo($"Started Companion process {process.Id}");
            return new CompanionProcess(process);
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning($"Could not start Companion: {error.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        int processId = _process.Id;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                _process.WaitForExit(2000);
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning($"Could not stop Companion: {error.Message}");
        }
        _process.Dispose();
        DeleteRuntimeMetadata(processId);
    }

    public static bool IsRuntimeRunning()
    {
        string runtimePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LilithAI",
            "runtime.json");
        if (!File.Exists(runtimePath))
        {
            return false;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(runtimePath));
            if (!document.RootElement.TryGetProperty("pid", out JsonElement value) || !value.TryGetInt32(out int pid))
            {
                return false;
            }
            Process process = Process.GetProcessById(pid);
            return !process.HasExited && string.Equals(process.ProcessName, "node", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            DeleteRuntimeMetadata(null);
            return false;
        }
    }

    private static void DeleteRuntimeMetadata(int? expectedProcessId)
    {
        string runtimePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LilithAI",
            "runtime.json");
        try
        {
            if (!File.Exists(runtimePath))
            {
                return;
            }
            if (expectedProcessId.HasValue)
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(runtimePath));
                if (!document.RootElement.TryGetProperty("pid", out JsonElement value)
                    || !value.TryGetInt32(out int runtimeProcessId)
                    || runtimeProcessId != expectedProcessId.Value)
                {
                    return;
                }
            }
            File.Delete(runtimePath);
        }
        catch
        {
            // Stale metadata is harmless and will be replaced on the next start.
        }
    }

    private static string ResolveNodeExecutable(string companionRoot)
    {
        string configured = Environment.GetEnvironmentVariable("LILITH_NODE_PATH") ?? "";
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        string bundled = Path.Combine(companionRoot, "node.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        return "node";
    }
}
