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

    public static List<EngineDefinition> All => new()
    {
        new() { Id = "azure", DisplayName = "Azure", NeedsRegion = true },
        new() { Id = "openai", DisplayName = "OpenAI" },
        new() { Id = "elevenlabs", DisplayName = "ElevenLabs" },
        new() { Id = "google", DisplayName = "Google" },
        new() { Id = "polly", DisplayName = "AWS Polly", NeedsRegion = true, NeedsSecretKey = true },
        new() { Id = "cartesia", DisplayName = "Cartesia" },
        new() { Id = "deepgram", DisplayName = "Deepgram" },
        new() { Id = "watson", DisplayName = "IBM Watson" },
        new() { Id = "playht", DisplayName = "PlayHT" },
        new() { Id = "witai", DisplayName = "Wit.ai" },
        new() { Id = "gemini", DisplayName = "Gemini" },
        new() { Id = "hume", DisplayName = "Hume AI" },
        new() { Id = "xai", DisplayName = "xAI Grok" },
        new() { Id = "fishaudio", DisplayName = "Fish Audio" },
        new() { Id = "mistral", DisplayName = "Mistral" },
        new() { Id = "murf", DisplayName = "Murf" },
        new() { Id = "unrealspeech", DisplayName = "Unreal Speech" },
        new() { Id = "resemble", DisplayName = "Resemble" },
        new() { Id = "upliftai", DisplayName = "Uplift AI" },
        new() { Id = "modelslab", DisplayName = "Models Lab" },
    };
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
        set { _apiKey = value; OnPropertyChanged(); }
    }

    private string _region = "";
    public string Region
    {
        get => _region;
        set { _region = value; OnPropertyChanged(); }
    }

    public bool NeedsRegion { get; set; }

    public string NoVoicesRegName => $"No{System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Id)}Voices";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
