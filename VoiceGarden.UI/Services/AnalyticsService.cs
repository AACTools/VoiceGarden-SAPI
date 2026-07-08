using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Privacy-friendly analytics via PostHog (EU hosted).
/// Opt-in only. Anonymous machine ID. No PII, no text content, no API keys.
/// </summary>
public class AnalyticsService
{
    private const string ApiKey = "phc_tExS6nJkQJynVY7WQGrjfGhrJbPcgxW7dGhkgwXSvDhc";
    private const string Endpoint = "https://eu.i.posthog.com/capture/";
    private const string RegPath = @"SOFTWARE\VoiceGardenSAPIAdapter";
    private const string IdName = "AnalyticsId";
    private const string EnabledName = "AnalyticsEnabled";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static string? _cachedId;

    /// <summary>
    /// Whether analytics is enabled (opt-in, default false).
    /// </summary>
    public static bool IsEnabled
    {
        get => GetBool(EnabledName, false);
        set => SetBool(EnabledName, value);
    }

    /// <summary>
    /// Anonymous machine identifier. Generated on first use, stored in registry.
    /// </summary>
    public static string DistinctId
    {
        get
        {
            if (_cachedId != null) return _cachedId;
            _cachedId = GetString(IdName);
            if (string.IsNullOrEmpty(_cachedId))
            {
                _cachedId = Guid.NewGuid().ToString("N");
                SetString(IdName, _cachedId);
            }
            return _cachedId;
        }
    }

    /// <summary>
    /// Track an event. Fire-and-forget — never blocks the UI.
    /// Only sends if IsEnabled is true.
    /// </summary>
    public static void Track(string eventName, params (string key, object value)[] properties)
    {
        if (!IsEnabled) return;

        var payload = new
        {
            api_key = ApiKey,
            @event = eventName,
            properties = BuildProperties(properties),
            timestamp = DateTime.UtcNow.ToString("o")
        };

        _ = Task.Run(async () =>
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _http.PostAsync(Endpoint, content);
            }
            catch { /* analytics should never break the app */ }
        });
    }

    private static object BuildProperties((string key, object value)[] properties)
    {
        var dict = new System.Collections.Generic.Dictionary<string, object>
        {
            ["distinct_id"] = DistinctId,
            ["app_version"] = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown",
        };

        foreach (var (key, value) in properties)
        {
            if (!string.IsNullOrEmpty(key))
                dict[key] = value;
        }

        return dict;
    }

    // Registry helpers
    private static bool GetBool(string name, bool defaultVal = false)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        var val = key?.GetValue(name);
        return val is int i ? i != 0 : defaultVal;
    }

    private static void SetBool(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath, writable: true);
        key?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    private static string? GetString(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue(name) as string;
    }

    private static void SetString(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.String);
    }
}
