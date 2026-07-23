using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithMod;

internal static class CompanionTasks
{
    public static async void LoadHistory(
        CompanionClient client,
        string sessionId,
        CancellationToken cancellationToken,
        Action<string> onSuccess,
        Action<string> onError)
    {
        try
        {
            IReadOnlyList<CompanionHistoryMessage> history = await client
                .GetHistoryAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            onSuccess(JsonSerializer.Serialize(history));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            onError($"{error.GetType().Name}: {error.Message}");
        }
    }

    public static async void Send(
        CompanionClient client,
        string sessionId,
        string playerName,
        string message,
        CancellationToken cancellationToken,
        Action<string, string, int, string> onSuccess,
        Action<string> onError)
    {
        try
        {
            CompanionReply reply = await client.SendAsync(sessionId, playerName, message, cancellationToken).ConfigureAwait(false);
            onSuccess(reply.Text, reply.Emotion, reply.DurationMs, reply.Status);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            onError($"{error.GetType().Name}: {error.Message}");
        }
    }

    public static Task<ExternalMediaState> PollMedia(
        CompanionClient client,
        CancellationToken cancellationToken)
    {
        return client.GetMediaStateAsync(cancellationToken);
    }

    public static async void ResetSession(
        CompanionClient client,
        string sessionId,
        CancellationToken cancellationToken,
        Action onSuccess,
        Action<string> onError)
    {
        try
        {
            await client.ResetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            onSuccess();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            onError($"{error.GetType().Name}: {error.Message}");
        }
    }

    public static async void ForgetPlayer(
        CompanionClient client,
        string playerName,
        CancellationToken cancellationToken,
        Action onSuccess,
        Action<string> onError)
    {
        try
        {
            await client.ForgetPlayerAsync(playerName, cancellationToken).ConfigureAwait(false);
            onSuccess();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            onError($"{error.GetType().Name}: {error.Message}");
        }
    }

    public static async void SeedAssistant(
        CompanionClient client,
        string sessionId,
        string text,
        CancellationToken cancellationToken,
        Action onSuccess,
        Action<string> onError)
    {
        try
        {
            await client.SeedAssistantAsync(sessionId, text, cancellationToken).ConfigureAwait(false);
            onSuccess();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            onError($"{error.GetType().Name}: {error.Message}");
        }
    }
}
