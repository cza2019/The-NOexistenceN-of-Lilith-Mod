using System;
using System.IO;
using System.Threading.Tasks;
using LilithMod;

await VerifyMediaSingleFlight();
VerifyRuntimeCache();
Console.WriteLine("Runtime resource tests passed.");

static async Task VerifyMediaSingleFlight()
{
    var coordinator = new MediaPollCoordinator();
    if (!coordinator.TryBegin() || coordinator.TryBegin())
    {
        throw new InvalidOperationException("Only one media poll may run at a time.");
    }

    await Task.Delay(25);
    if (!coordinator.Complete()
        || coordinator.CurrentConcurrent != 0
        || coordinator.MaxConcurrent != 1
        || coordinator.Begun != 1
        || coordinator.Completed != 1)
    {
        throw new InvalidOperationException("Media poll accounting is inconsistent.");
    }

    if (!coordinator.TryBegin())
    {
        throw new InvalidOperationException("A completed poll must allow the next poll.");
    }
    coordinator.Cancel();
    if (coordinator.Complete() || coordinator.TryBegin() || !coordinator.Cancelled)
    {
        throw new InvalidOperationException("Cancellation must suppress callbacks and future polls.");
    }
}

static void VerifyRuntimeCache()
{
    string root = Path.Combine(Path.GetTempPath(), "LilithModTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string runtimePath = Path.Combine(root, "runtime.json");
    try
    {
        WriteRuntime(runtimePath, 6186, "token-one");
        var cache = new CompanionRuntimeCache(runtimePath);
        CompanionRuntimeEndpoint first = cache.Load();
        CompanionRuntimeEndpoint unchanged = cache.Load();
        if (!ReferenceEquals(first, unchanged) || cache.ParseCount != 1)
        {
            throw new InvalidOperationException("Unchanged runtime metadata must be parsed once.");
        }

        WriteRuntime(runtimePath, 6199, "token-two-longer");
        CompanionRuntimeEndpoint changed = cache.Load();
        if (changed.Port != 6199 || changed.Token != "token-two-longer" || cache.ParseCount != 2)
        {
            throw new InvalidOperationException("Port and token changes must invalidate the endpoint cache.");
        }

        cache.Invalidate();
        _ = cache.Load();
        if (cache.ParseCount != 3)
        {
            throw new InvalidOperationException("Explicit invalidation must force one metadata refresh.");
        }

        File.WriteAllText(runtimePath, "{not-json");
        ExpectFailure(() => _ = cache.Load(), "Temporarily unreadable metadata must fail safely.");
        WriteRuntime(runtimePath, 6200, "recovered-token");
        CompanionRuntimeEndpoint recovered = cache.Load();
        if (recovered.Port != 6200 || recovered.Token != "recovered-token")
        {
            throw new InvalidOperationException("The endpoint cache did not recover after a temporary failure.");
        }

        File.WriteAllText(runtimePath, "{\"host\":\"example.com\",\"port\":6200,\"token\":\"bad\"}");
        ExpectFailure(() => _ = cache.Load(), "Non-loopback runtime endpoints must be rejected.");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void WriteRuntime(string path, int port, string token)
{
    File.WriteAllText(path, $"{{\"host\":\"127.0.0.1\",\"port\":{port},\"token\":\"{token}\"}}");
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
}

static void ExpectFailure(Action action, string message)
{
    try
    {
        action();
    }
    catch
    {
        return;
    }
    throw new InvalidOperationException(message);
}
