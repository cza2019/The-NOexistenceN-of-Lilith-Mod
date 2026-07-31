using System;
using System.IO;
using System.Threading.Tasks;
using LilithMod;

await VerifyMediaSingleFlight();
VerifyRuntimeCache();
VerifyStartupUpdateCoordinator();
VerifyUpdateReleaseUrl();
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

static void VerifyStartupUpdateCoordinator()
{
    var coordinator = new StartupUpdateCoordinator();
    coordinator.Start(100f);
    coordinator.Start(200f);
    if (coordinator.TryBegin(104.99f)
        || !coordinator.TryBegin(105f)
        || coordinator.TryBegin(105f)
        || !coordinator.Pending)
    {
        throw new InvalidOperationException("The startup update check must begin once after five seconds.");
    }
    if (!coordinator.RetryAfterCompanionUnavailable(105f)
        || coordinator.Pending
        || coordinator.NextAttempt != 107f
        || coordinator.TryBegin(106.99f)
        || !coordinator.TryBegin(107f))
    {
        throw new InvalidOperationException("Companion readiness retries must use a two-second interval.");
    }
    if (!coordinator.Complete()
        || !coordinator.Completed
        || coordinator.TryBegin(108f)
        || coordinator.Complete())
    {
        throw new InvalidOperationException("A structured update result must complete the launch check once.");
    }

    var expired = new StartupUpdateCoordinator();
    expired.Start(0f);
    if (expired.TryBegin(20.01f) || !expired.Completed)
    {
        throw new InvalidOperationException("Companion readiness must stop after twenty seconds.");
    }

    var cancelled = new StartupUpdateCoordinator();
    cancelled.Start(0f);
    cancelled.Cancel();
    if (cancelled.TryBegin(5f) || !cancelled.Completed)
    {
        throw new InvalidOperationException("Game shutdown must cancel the startup update check.");
    }
}

static void VerifyUpdateReleaseUrl()
{
    const string valid =
        "https://github.com/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/tag/v0.3.2";
    Uri? accepted = UpdateReleaseUrl.Select(true, valid);
    if (accepted is null || accepted.AbsoluteUri != valid)
    {
        throw new InvalidOperationException("A confirmed official GitHub Release URL must be accepted.");
    }
    if (UpdateReleaseUrl.Select(false, valid) is not null)
    {
        throw new InvalidOperationException("Choosing No must never open the Release URL.");
    }
    foreach (string blocked in new[]
    {
        "http://github.com/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/tag/v0.3.2",
        "https://evil.example/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/tag/v0.3.2",
        "https://github.com/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/latest",
        "https://github.com/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/tag/",
        "https://github.com@evil.example/cza2019/The-NOexistenceN-of-Lilith-Mod/releases/tag/v0.3.2"
    })
    {
        if (UpdateReleaseUrl.Select(true, blocked) is not null)
        {
            throw new InvalidOperationException($"Unsafe update URL was accepted: {blocked}");
        }
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
