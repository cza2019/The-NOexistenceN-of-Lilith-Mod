using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LilithMod;

internal sealed record CompanionRuntimeEndpoint(string Host, int Port, string Token);

internal sealed class CompanionRuntimeCache
{
    private readonly object _sync = new();
    private readonly string _runtimePath;
    private CompanionRuntimeEndpoint? _cached;
    private DateTime _cachedWriteTimeUtc;
    private long _cachedLength = -1;

    public CompanionRuntimeCache(string runtimePath)
    {
        _runtimePath = runtimePath;
    }

    internal int ParseCount { get; private set; }

    public CompanionRuntimeEndpoint Load()
    {
        lock (_sync)
        {
            var file = new FileInfo(_runtimePath);
            if (!file.Exists)
            {
                throw new InvalidOperationException("Companion is not running");
            }

            if (_cached is not null
                && file.LastWriteTimeUtc == _cachedWriteTimeUtc
                && file.Length == _cachedLength)
            {
                return _cached;
            }

            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(_runtimePath, Encoding.UTF8));
            JsonElement root = document.RootElement;
            string host = ReadString(root, "host", "127.0.0.1");
            int port = root.TryGetProperty("port", out JsonElement portValue)
                && portValue.TryGetInt32(out int parsedPort)
                    ? parsedPort
                    : 6186;
            string token = ReadString(root, "token");
            if (host != "127.0.0.1" && host != "::1" && host != "localhost")
            {
                throw new InvalidOperationException("Companion runtime is not loopback-only");
            }
            if (port is < 1 or > 65535 || string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Companion runtime metadata is invalid");
            }

            _cached = new CompanionRuntimeEndpoint(host, port, token);
            _cachedWriteTimeUtc = file.LastWriteTimeUtc;
            _cachedLength = file.Length;
            ParseCount++;
            return _cached;
        }
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _cached = null;
            _cachedWriteTimeUtc = default;
            _cachedLength = -1;
        }
    }

    private static string ReadString(JsonElement element, string property, string fallback = "")
    {
        return element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
    }
}
