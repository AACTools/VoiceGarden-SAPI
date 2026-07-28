using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoiceGarden.UI.Models;

/// <summary>
/// Hardcoded configuration constants (was branding.json).
/// </summary>
public static class BrandingConfig
{
    public const string AppName = "VoiceGarden";
    public const string InstallDir = "VoiceGardenSAPI";
    public const bool DefaultSherpaEnabled = true;
    public const bool DefaultAzureEnabled = false;
}

public class EngineDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool NeedsRegion { get; set; } = false;
    public bool NeedsSecretKey { get; set; } = false;
    /// <summary>Credential key names from the Rust wrapper (e.g. ["subscriptionKey","region"])</summary>
    public string[] CredentialKeys { get; set; } = Array.Empty<string>();

    private static List<EngineDefinition>? _cachedEngines;

    /// <summary>
    /// Discover all available cloud engines from the Rust wrapper.
    /// Cached after first call. Falls back to a comprehensive list if discovery fails.
    /// </summary>
    public static List<EngineDefinition> DiscoverAll()
    {
        if (_cachedEngines != null) return _cachedEngines;

        var result = new List<EngineDefinition>();
        try
        {
            var engines = RustTtsWrapper.TtsClient.ListEngines();
            foreach (var e in engines)
            {
                if (!e.NeedsCredentials) continue;

                var keys = ParseCredentialKeys(e.CredentialKeysJson);
                result.Add(new EngineDefinition
                {
                    Id = e.Id ?? "",
                    DisplayName = e.Name ?? e.Id ?? "",
                    NeedsRegion = keys.Contains("region") || keys.Contains("userId"),
                    NeedsSecretKey = keys.Contains("secretAccessKey"),
                    CredentialKeys = keys,
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Engine discovery failed: {ex.Message}");
        }

        // If discovery returned nothing, use fallback
        if (result.Count == 0)
        {
            result = FallbackEngines();
        }

        _cachedEngines = result;
        return result;
    }

    private static List<EngineDefinition> FallbackEngines() => new()
    {
        new() { Id = "azure", DisplayName = "Azure", NeedsRegion = true, CredentialKeys = new[] { "subscriptionKey", "region" } },
        new() { Id = "openai", DisplayName = "OpenAI", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "elevenlabs", DisplayName = "ElevenLabs", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "google", DisplayName = "Google", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "polly", DisplayName = "AWS Polly", NeedsRegion = true, NeedsSecretKey = true, CredentialKeys = new[] { "accessKeyId", "secretAccessKey", "region" } },
        new() { Id = "cartesia", DisplayName = "Cartesia", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "deepgram", DisplayName = "Deepgram", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "playht", DisplayName = "PlayHT", CredentialKeys = new[] { "apiKey", "userId" } },
        new() { Id = "fishaudio", DisplayName = "Fish Audio", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "hume", DisplayName = "Hume AI", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "mistral", DisplayName = "Mistral", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "murf", DisplayName = "Murf", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "resemble", DisplayName = "Resemble", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "unrealspeech", DisplayName = "Unreal Speech", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "upliftai", DisplayName = "Uplift AI", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "watson", DisplayName = "IBM Watson", NeedsRegion = true, CredentialKeys = new[] { "apiKey", "region", "instanceId" } },
        new() { Id = "witai", DisplayName = "Wit.ai", CredentialKeys = new[] { "token" } },
        new() { Id = "xai", DisplayName = "xAI", CredentialKeys = new[] { "apiKey" } },
        new() { Id = "modelslab", DisplayName = "ModelsLab", CredentialKeys = new[] { "apiKey" } },
    };

    private static string[] ParseCredentialKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new[] { "apiKey" };
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? new[] { "apiKey" };
        }
        catch
        {
            return new[] { "apiKey" };
        }
    }

    [Obsolete("Use DiscoverAll() instead — queries the Rust wrapper for available engines")]
    public static List<EngineDefinition> All => DiscoverAll();
}

public class CloudEngineSetting : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    private string _apiKey = "";
    public string ApiKey
    {
        get => _apiKey;
        set { _apiKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasKey)); }
    }

    private string _region = "";
    public string Region
    {
        get => _region;
        set { _region = value; OnPropertyChanged(); }
    }

    public bool NeedsRegion { get; set; }

    private string _verificationStatus = "";
    public string VerificationStatus
    {
        get => _verificationStatus;
        set { _verificationStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(VerificationIcon)); OnPropertyChanged(nameof(VerificationDetail)); }
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Just the icon: ✓, ✗, or ?</summary>
    public string VerificationIcon => _verificationStatus switch
    {
        var s when s.StartsWith("✓") => "✓",
        var s when s.StartsWith("✗") => "✗",
        var s when s.StartsWith("Checking") => "?",
        _ => "",
    };

    /// <summary>The detail text without the icon prefix</summary>
    public string VerificationDetail => _verificationStatus switch
    {
        var s when s.StartsWith("✓ ") => s[2..],
        var s when s.StartsWith("✗ ") => s[2..],
        _ => _verificationStatus == "Checking..." ? "Checking..." : "",
    };

    public string NoVoicesRegName => $"No{System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Id)}Voices";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
