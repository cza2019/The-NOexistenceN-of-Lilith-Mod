using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using BepInEx;

namespace LilithMod;

internal static class ModLocalization
{
    public const string DefaultLocale = "zh-CN";
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "zh-CN", "zh-TW", "en", "ja"
    };
    private static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LilithAI");
    private static readonly string ConfigPath = Path.Combine(DataRoot, "config.json");
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _configWriteTimeUtc;
    private static string _languageMode = "follow_game";
    private static string _lastLocale = "";
    private static DateTime _nextConfigCheckUtc;

    public static string CurrentLocale
    {
        get
        {
            RefreshConfiguration();
            string locale = string.Equals(_languageMode, "follow_game", StringComparison.OrdinalIgnoreCase)
                ? GameLocale
                : NormalizeLocale(_languageMode, DefaultLocale);
            if (!string.Equals(locale, _lastLocale, StringComparison.Ordinal))
            {
                _lastLocale = locale;
                LilithModPlugin.PluginLog?.LogInfo($"Mod locale selected: {locale}; mode={_languageMode}");
            }
            return locale;
        }
    }

    public static string GameLocale
    {
        get
        {
            try
            {
                string raw = GameSetting.Language ?? "";
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    return NormalizeLocale(raw, SystemLocale);
                }
            }
            catch
            {
                // The game singleton is not always ready during early startup.
            }
            return SystemLocale;
        }
    }

    public static string SystemLocale => NormalizeLocale(
        CultureInfo.CurrentUICulture.Name, DefaultLocale);

    public static string NormalizeLocale(string? value, string fallback = DefaultLocale)
    {
        string raw = (value ?? "").Trim().Replace('_', '-');
        if (raw.Length == 0)
        {
            return fallback;
        }
        string lower = raw.ToLowerInvariant();
        if (lower is "zh-tw" or "zh-hant" or "zh-hk" or "zh-mo" or "tw" or "tc"
            || lower.Contains("traditional", StringComparison.Ordinal))
        {
            return "zh-TW";
        }
        if (lower is "zh-cn" or "zh-hans" or "zh-sg" or "cn" or "sc"
            || lower.Contains("simplified", StringComparison.Ordinal))
        {
            return "zh-CN";
        }
        if (lower is "ja" or "jp" or "japanese" || lower.StartsWith("ja-", StringComparison.Ordinal))
        {
            return "ja";
        }
        if (lower is "en" or "english" || lower.StartsWith("en-", StringComparison.Ordinal))
        {
            return "en";
        }
        return Supported.Contains(raw) ? raw : fallback;
    }

    public static string T(string key, params object[] args)
    {
        string locale = CurrentLocale;
        string value = ReadValue(locale, key) ?? ReadValue(DefaultLocale, key) ?? key;
        try
        {
            return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static void RefreshConfiguration()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextConfigCheckUtc)
        {
            return;
        }
        _nextConfigCheckUtc = now.AddSeconds(1);
        try
        {
            DateTime writeTime = File.Exists(ConfigPath)
                ? File.GetLastWriteTimeUtc(ConfigPath)
                : DateTime.MinValue;
            if (writeTime == _configWriteTimeUtc)
            {
                return;
            }
            _configWriteTimeUtc = writeTime;
            if (writeTime == DateTime.MinValue)
            {
                _languageMode = "follow_game";
                return;
            }
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            if (document.RootElement.TryGetProperty("ui", out JsonElement ui)
                && ui.TryGetProperty("language_mode", out JsonElement mode)
                && mode.ValueKind == JsonValueKind.String)
            {
                string configured = mode.GetString() ?? "follow_game";
                _languageMode = configured.Equals("follow_game", StringComparison.OrdinalIgnoreCase)
                    ? "follow_game"
                    : NormalizeLocale(configured, "follow_game");
            }
            else
            {
                _languageMode = "follow_game";
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog?.LogDebug($"Could not refresh Mod locale: {error.Message}");
        }
    }

    private static string? ReadValue(string locale, string key)
    {
        Dictionary<string, string> catalog = LoadCatalog(locale);
        return catalog.TryGetValue(key, out string? value) ? value : null;
    }

    private static Dictionary<string, string> LoadCatalog(string locale)
    {
        locale = NormalizeLocale(locale);
        if (Catalogs.TryGetValue(locale, out Dictionary<string, string>? cached))
        {
            return cached;
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            string path = Path.Combine(Paths.PluginPath, "locales", locale + ".json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("strings", out JsonElement strings))
            {
                foreach (JsonProperty property in strings.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        result[property.Name] = property.Value.GetString() ?? "";
                    }
                }
            }
        }
        catch (Exception error)
        {
            LilithModPlugin.PluginLog?.LogWarning(
                $"Could not load locale catalog {locale}: {error.GetType().Name}: {error.Message}");
        }
        Catalogs[locale] = result;
        return result;
    }
}
