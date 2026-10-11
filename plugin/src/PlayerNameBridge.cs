using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using UI.TraySetting;
using UnityEngine;

namespace LilithMod;

/// <summary>
/// Bridges the out-of-process settings window to the game's native player-name
/// API. Requests are scoped to one game process so a stale file can never rename
/// the player on a later launch.
/// </summary>
internal sealed class PlayerNameBridge : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _statePath;
    private readonly string _requestPath;
    private readonly string _resultPath;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly int _processId = Process.GetCurrentProcess().Id;
    private DateTime _nextPollUtc;
    private DateTime _nextStateWriteUtc;
    private string _lastPlayerName = "";
    private bool _disposed;

    public PlayerNameBridge()
    {
        string dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LilithAI");
        Directory.CreateDirectory(dataRoot);
        _statePath = Path.Combine(dataRoot, "game-state.json");
        _requestPath = Path.Combine(dataRoot, "player-name-request.json");
        _resultPath = Path.Combine(dataRoot, "player-name-result.json");

        DeleteTransientFile(_requestPath);
        DeleteTransientFile(_resultPath);
        Tick();
    }

    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        string currentName = CurrentPlayerName();
        if (!string.Equals(currentName, _lastPlayerName, StringComparison.Ordinal)
            || now >= _nextStateWriteUtc)
        {
            _lastPlayerName = currentName;
            WriteState(running: true, currentName);
            _nextStateWriteUtc = now.AddSeconds(1);
        }

        if (now < _nextPollUtc)
        {
            return;
        }

        _nextPollUtc = now.AddMilliseconds(250);
        ProcessPendingRequest(now);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WriteState(running: false, _lastPlayerName);
        DeleteTransientFile(_requestPath);
        DeleteTransientFile(_resultPath);
    }

    private void ProcessPendingRequest(DateTime now)
    {
        if (!File.Exists(_requestPath))
        {
            return;
        }

        PlayerNameRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<PlayerNameRequest>(
                File.ReadAllText(_requestPath),
                JsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                throw new InvalidDataException(ModLocalization.T("bridge.invalid_request"));
            }

            if (!string.Equals(request.SessionId, _sessionId, StringComparison.Ordinal))
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    CurrentPlayerName(),
                    ModLocalization.T("bridge.stale_session")));
                return;
            }

            if (!DateTime.TryParse(
                    request.CreatedAt,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTime createdAt)
                || (now - createdAt.ToUniversalTime()).Duration() > TimeSpan.FromSeconds(30))
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    CurrentPlayerName(),
                    ModLocalization.T("bridge.expired")));
                return;
            }

            string currentName = CurrentPlayerName();
            if (!string.Equals(currentName, request.ExpectedName?.Trim(), StringComparison.Ordinal))
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    currentName,
                    ModLocalization.T("bridge.name_changed")));
                return;
            }

            string requestedName = request.RequestedName?.Trim() ?? "";
            if (requestedName.Length == 0 || requestedName.Length > 128)
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    currentName,
                    ModLocalization.T("bridge.invalid_length")));
                return;
            }

            string canonicalName = NormalizeWithGameRules(requestedName);
            if (canonicalName.Length == 0 || !PlayerNameRule.MeetsMinimum(canonicalName))
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    currentName,
                    ModLocalization.T("bridge.invalid_name")));
                return;
            }

            string operation = request.Operation?.Trim().ToLowerInvariant() ?? "";
            if (operation == "validate")
            {
                WriteResult(PlayerNameResult.Success(
                    request,
                    currentName,
                    canonicalName,
                    changed: false));
                return;
            }

            if (operation != "apply")
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    currentName,
                    ModLocalization.T("bridge.unsupported")));
                return;
            }

            bool changed = !string.Equals(currentName, canonicalName, StringComparison.Ordinal);
            if (changed)
            {
                TraySettingModel model = new();
                _ = model.SavePlayerName(requestedName);
            }

            string appliedName = CurrentPlayerName();
            if (!string.Equals(appliedName, canonicalName, StringComparison.Ordinal))
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    appliedName,
                    ModLocalization.T("bridge.save_rejected")));
                return;
            }

            RefreshOpenSettingsView(appliedName);
            _lastPlayerName = appliedName;
            WriteState(running: true, appliedName);
            _nextStateWriteUtc = now.AddSeconds(1);
            WriteResult(PlayerNameResult.Success(
                request,
                currentName,
                appliedName,
                changed));
            LilithModPlugin.PluginLog.LogInfo(
                $"Player name updated through Mod settings: changed={changed}");
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogWarning($"Player-name request failed: {error}");
            if (request is not null)
            {
                WriteResult(PlayerNameResult.Failure(
                    request,
                    CurrentPlayerName(),
                    ModLocalization.T("bridge.failed", error.Message)));
            }
        }
        finally
        {
            DeleteTransientFile(_requestPath);
        }
    }

    private static string NormalizeWithGameRules(string playerName)
    {
        string normalized = TraySettingModel.NormalizePlayerName(playerName) ?? "";
        return normalized.Trim();
    }

    private static string CurrentPlayerName()
    {
        try
        {
            return NormalizeDisplayName(Archive.Instance?.playerName ?? "");
        }
        catch
        {
            return "";
        }
    }

    private static string NormalizeDisplayName(string value) => (value ?? "").Trim();

    private static void RefreshOpenSettingsView(string playerName)
    {
        try
        {
            // The legacy controller no longer exists in the new native settings
            // UI. Keep this optional refresh compatible with older game builds;
            // saving the name through TraySettingModel remains authoritative.
            Type? controllerType = typeof(TraySettingModel).Assembly.GetType(
                "UI.TraySetting.TraySettingController");
            if (controllerType is null) return;
            MethodInfo? find = Array.Find(typeof(UnityEngine.Object).GetMethods(), method =>
                method.Name == "FindObjectOfType" && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
            object? controller = find?.MakeGenericMethod(controllerType).Invoke(null, null);
            if (controller is null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object? view = controllerType.GetProperty("_view", flags)?.GetValue(controller)
                ?? controllerType.GetField("_view", flags)?.GetValue(controller);
            view?.GetType().GetMethod("SetNameWithoutNotify", flags, null,
                new[] { typeof(string) }, null)?.Invoke(view, new object[] { playerName });
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogDebug(
                $"Could not refresh the open native settings view: {error.Message}");
        }
    }

    private void WriteState(bool running, string playerName)
    {
        try
        {
            WriteJsonAtomic(_statePath, new PlayerNameGameState
            {
                SchemaVersion = 1,
                SessionId = _sessionId,
                ProcessId = _processId,
                Running = running,
                PlayerName = playerName,
                GameLanguage = ModLocalization.GameLocale,
                UpdatedAt = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog.LogDebug($"Could not write game state: {error.Message}");
        }
    }

    private void WriteResult(PlayerNameResult result)
    {
        WriteJsonAtomic(_resultPath, result);
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            DeleteTransientFile(tempPath);
        }
    }

    private static void DeleteTransientFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A later poll or launch will safely ignore the session-scoped file.
        }
    }

    private sealed class PlayerNameGameState
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("process_id")]
        public int ProcessId { get; set; }

        [JsonPropertyName("running")]
        public bool Running { get; set; }

        [JsonPropertyName("player_name")]
        public string PlayerName { get; set; } = "";

        [JsonPropertyName("game_language")]
        public string GameLanguage { get; set; } = ModLocalization.DefaultLocale;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = "";
    }

    private sealed class PlayerNameRequest
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("request_id")]
        public string RequestId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("operation")]
        public string Operation { get; set; } = "";

        [JsonPropertyName("expected_name")]
        public string ExpectedName { get; set; } = "";

        [JsonPropertyName("requested_name")]
        public string RequestedName { get; set; } = "";

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = "";
    }

    private sealed class PlayerNameResult
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("request_id")]
        public string RequestId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("success")]
        public bool IsSuccess { get; set; }

        [JsonPropertyName("changed")]
        public bool Changed { get; set; }

        [JsonPropertyName("old_name")]
        public string OldName { get; set; } = "";

        [JsonPropertyName("applied_name")]
        public string AppliedName { get; set; } = "";

        [JsonPropertyName("error")]
        public string Error { get; set; } = "";

        [JsonPropertyName("completed_at")]
        public string CompletedAt { get; set; } = "";

        public static PlayerNameResult Success(
            PlayerNameRequest request,
            string oldName,
            string appliedName,
            bool changed) => new()
            {
                RequestId = request.RequestId,
                SessionId = request.SessionId,
                IsSuccess = true,
                Changed = changed,
                OldName = oldName,
                AppliedName = appliedName,
                CompletedAt = DateTime.UtcNow.ToString("O")
            };

        public static PlayerNameResult Failure(
            PlayerNameRequest request,
            string currentName,
            string error) => new()
            {
                RequestId = request.RequestId,
                SessionId = request.SessionId,
                IsSuccess = false,
                OldName = currentName,
                AppliedName = currentName,
                Error = error,
                CompletedAt = DateTime.UtcNow.ToString("O")
            };
    }
}
