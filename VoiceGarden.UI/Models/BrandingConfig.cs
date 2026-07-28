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

    /// <summary>
    /// Discover all available cloud engines from the Rust wrapper.
    /// This is the single source of truth — no hardcoded engine lists.
    /// </summary>
    public static List<EngineDefinition> DiscoverAll()
    {
        var result = new List<EngineDefinition>();
        try
        {
            var engines = RustTtsWrapper.TtsClient.ListEngines();
            foreach (var e in engines)
            {
                // Skip built-in engines that don't need credentials
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
            // Fallback to minimal hardcoded list if discovery fails
            System.Diagnostics.Debug.WriteLine($"Engine discovery failed: {ex.Message}");
            result.Add(new EngineDefinition { Id = "azure", DisplayName = "Azure", NeedsRegion = true, CredentialKeys = new[] { "subscriptionKey", "region" } });
            result.Add(new EngineDefinition { Id = "openai", DisplayName = "OpenAI", CredentialKeys = new[] { "apiKey" } });
        }
        return result;
    }

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
        set { _apiKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasKey)); OnPropertyChanged(nameof(VerificationText)); }
    }

    private string _region = "";
    public string Region
    {
        get => _region;
        set { _region = value; OnPropertyChanged(); }
    }

    public bool NeedsRegion { get; set; }

    private string _verificationStatus = ""; // "", "✓ Valid", "✗ Invalid: ...", "Checking..."
    public string VerificationStatus
    {
        get => _verificationStatus;
        set { _verificationStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(VerificationText)); }
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);
    public string VerificationText => string.IsNullOrEmpty(VerificationStatus) ? "" : VerificationStatus;

    public string NoVoicesRegName => $"No{System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Id)}Voices";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
