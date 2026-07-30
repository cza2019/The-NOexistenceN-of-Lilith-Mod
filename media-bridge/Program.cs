using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Media.Control;

namespace LilithMediaBridge;

internal static class Program
{
    private static readonly string[] DefaultMusicSources =
    {
        "cloudmusic",
        "netease",
        "qqmusic",
        "spotify",
        "music.ui",
        "applemusic",
        "appleinc.applemusicwin",
        "amazonmusic",
        "amazon music",
        "amazonmobilellc.amazonmusic"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<int> Main(string[] args)
    {
        bool once = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
        int? parentPid = ReadParentPid(args);
        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception error)
        {
            Write(new MediaSnapshot(false, false, "", "", "", "unavailable", error.GetType().Name));
            return 2;
        }

        string lastJson = "";
        do
        {
            if (parentPid.HasValue && !IsProcessRunning(parentPid.Value))
            {
                return 0;
            }

            MediaSnapshot snapshot = await ReadSnapshot(manager, ReadAllowedSources(args));
            string json = JsonSerializer.Serialize(snapshot, MediaJsonContext.Default.MediaSnapshot);
            if (!string.Equals(json, lastJson, StringComparison.Ordinal))
            {
                Console.WriteLine(json);
                Console.Out.Flush();
                lastJson = json;
            }

            if (!once)
            {
                await Task.Delay(750);
            }
        }
        while (!once);

        return 0;
    }

    private static async Task<MediaSnapshot> ReadSnapshot(
        GlobalSystemMediaTransportControlsSessionManager manager,
        IReadOnlyList<string> allowedSources)
    {
        try
        {
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = manager.GetSessions();
            GlobalSystemMediaTransportControlsSession[] musicSessions = sessions
                .Where(session => IsAllowedSource(session.SourceAppUserModelId, allowedSources))
                .ToArray();
            GlobalSystemMediaTransportControlsSession? selected = musicSessions
                .FirstOrDefault(session => session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? musicSessions.FirstOrDefault(session => ReferenceEquals(session, manager.GetCurrentSession()))
                ?? musicSessions.FirstOrDefault();
            if (selected is null)
            {
                return new MediaSnapshot(true, false, "", "", "", "ignored", "");
            }

            GlobalSystemMediaTransportControlsSessionPlaybackStatus status = selected.GetPlaybackInfo().PlaybackStatus;
            GlobalSystemMediaTransportControlsSessionMediaProperties properties = await selected.TryGetMediaPropertiesAsync();
            return new MediaSnapshot(
                true,
                status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                selected.SourceAppUserModelId ?? "",
                properties?.Title ?? "",
                properties?.Artist ?? "",
                status.ToString().ToLowerInvariant(),
                "");
        }
        catch (Exception error)
        {
            return new MediaSnapshot(false, false, "", "", "", "unavailable", error.GetType().Name);
        }
    }

    private static IReadOnlyList<string> ReadAllowedSources(string[] args)
    {
        var values = new List<string>();
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], "--allow-source", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                values.Add(args[index + 1].Trim());
            }
        }
        return values.Count > 0 ? values : DefaultMusicSources;
    }

    private static bool IsAllowedSource(string? source, IReadOnlyList<string> allowedSources)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }
        return allowedSources.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static int? ReadParentPid(string[] args)
    {
        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], "--parent-pid", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[index + 1], out int pid)
                && pid > 0)
            {
                return pid;
            }
        }
        return null;
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            return !System.Diagnostics.Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void Write(MediaSnapshot snapshot)
    {
        Console.WriteLine(JsonSerializer.Serialize(snapshot, MediaJsonContext.Default.MediaSnapshot));
    }
}

internal sealed record MediaSnapshot(
    bool Available,
    bool Playing,
    string Source,
    string Title,
    string Artist,
    string PlaybackStatus,
    string Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(MediaSnapshot))]
internal partial class MediaJsonContext : JsonSerializerContext
{
}
