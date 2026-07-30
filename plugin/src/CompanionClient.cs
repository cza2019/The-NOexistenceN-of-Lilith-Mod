using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithMod;

internal sealed class CompanionClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(40)
    };

    private readonly string _runtimePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LilithAI",
        "runtime.json");
    private readonly CompanionRuntimeCache _runtime;

    public CompanionClient()
    {
        _runtime = new CompanionRuntimeCache(_runtimePath);
    }

    internal void InvalidateRuntime()
    {
        _runtime.Invalidate();
    }

    public async Task<CompanionReply> SendAsync(
        string sessionId,
        string playerName,
        string message,
        string locale,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            protocol_version = 1,
            request_id = Guid.NewGuid().ToString("N"),
            player = new { name = playerName },
            mode = "in_character",
            locale = ModLocalization.NormalizeLocale(locale),
            message
        };

        RuntimeResponse response = await SendRuntimeRequestAsync(
            HttpMethod.Post,
            $"/v1/session/{Uri.EscapeDataString(sessionId)}/message",
            JsonSerializer.Serialize(payload),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Companion HTTP {(int)response.StatusCode}");
        }

        using JsonDocument document = JsonDocument.Parse(response.Json);
        JsonElement root = document.RootElement;
        string text = ReadString(root, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Companion returned an empty reply");
        }

        string emotion = "neutral";
        int durationMs = 3000;
        if (root.TryGetProperty("ui", out JsonElement ui))
        {
            emotion = ReadString(ui, "emotion", "neutral");
            if (ui.TryGetProperty("duration_ms", out JsonElement duration) && duration.TryGetInt32(out int parsed))
            {
                durationMs = Math.Clamp(parsed, 500, 45000);
            }
        }

        IReadOnlyList<ChatCitation> citations = CompanionCitationParser.Parse(root);
        return new CompanionReply(
            text.Trim(),
            emotion,
            durationMs,
            ReadString(root, "status", "ok"),
            citations);
    }

    public async Task<ExternalMediaState> GetMediaStateAsync(CancellationToken cancellationToken)
    {
        RuntimeResponse response = await SendRuntimeRequestAsync(
            HttpMethod.Get,
            "/v1/media",
            null,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return ExternalMediaState.Unavailable;
        }

        using JsonDocument document = JsonDocument.Parse(response.Json);
        JsonElement root = document.RootElement;
        bool playing = root.TryGetProperty("playing", out JsonElement value) && value.ValueKind == JsonValueKind.True;
        return new ExternalMediaState(
            playing,
            ReadString(root, "source"),
            ReadString(root, "title"),
            true);
    }

    public async Task<IReadOnlyList<CompanionHistoryMessage>> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        RuntimeResponse response = await SendRuntimeRequestAsync(
            HttpMethod.Get,
            $"/v1/session/{Uri.EscapeDataString(sessionId)}/history",
            null,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Companion HTTP {(int)response.StatusCode}");
        }

        using JsonDocument document = JsonDocument.Parse(response.Json);
        var messages = new List<CompanionHistoryMessage>();
        if (!document.RootElement.TryGetProperty("messages", out JsonElement history)
            || history.ValueKind != JsonValueKind.Array)
        {
            return messages;
        }

        foreach (JsonElement item in history.EnumerateArray())
        {
            string role = ReadString(item, "role");
            string content = ReadString(item, "content").Trim();
            if ((role == "user" || role == "assistant") && content.Length > 0)
            {
                messages.Add(new CompanionHistoryMessage(role, content));
            }
        }
        return messages;
    }

    public async Task ResetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        await SendControlAsync(new
        {
            action = "reset_session",
            session_id = sessionId
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ForgetPlayerAsync(string playerName, CancellationToken cancellationToken)
    {
        await SendControlAsync(new
        {
            action = "forget_player",
            player_name = playerName
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SeedAssistantAsync(
        string sessionId,
        string text,
        CancellationToken cancellationToken)
    {
        await SendControlAsync(new
        {
            action = "seed_assistant",
            session_id = sessionId,
            text
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendControlAsync(object payload, CancellationToken cancellationToken)
    {
        RuntimeResponse response = await SendRuntimeRequestAsync(
            HttpMethod.Post,
            "/v1/control",
            JsonSerializer.Serialize(payload),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Companion HTTP {(int)response.StatusCode}");
        }
        using JsonDocument document = JsonDocument.Parse(response.Json);
        if (!document.RootElement.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException("Companion did not confirm the control action");
        }
    }

    private async Task<RuntimeResponse> SendRuntimeRequestAsync(
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            CompanionRuntimeEndpoint runtime = _runtime.Load();
            string target = $"http://{runtime.Host}:{runtime.Port}{path}";
            try
            {
                using var request = new HttpRequestMessage(method, target);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.Token);
                if (json is not null)
                {
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (attempt == 0
                    && (response.StatusCode == HttpStatusCode.Unauthorized
                        || response.StatusCode == HttpStatusCode.Forbidden))
                {
                    _runtime.Invalidate();
                    continue;
                }
                return new RuntimeResponse(response.StatusCode, response.IsSuccessStatusCode, responseJson);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                _runtime.Invalidate();
            }
        }

        throw new InvalidOperationException("Companion connection failed after refreshing runtime metadata");
    }

    private static string ReadString(JsonElement element, string property, string fallback = "")
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private sealed record RuntimeResponse(HttpStatusCode StatusCode, bool IsSuccessStatusCode, string Json);
}

internal sealed record CompanionReply(
    string Text,
    string Emotion,
    int DurationMs,
    string Status,
    IReadOnlyList<ChatCitation> Citations);

internal sealed record CompanionHistoryMessage(string Role, string Content);

internal sealed record ExternalMediaState(bool Playing, string Source, string Title, bool Available)
{
    public static ExternalMediaState Unavailable { get; } = new(false, "", "", false);
}
