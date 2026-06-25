using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text.Json;

namespace VoiceGarden.UI.Models;

public class BrandingConfig
{
    public string AppName { get; set; } = "VoiceGarden";
    public string InstallDir { get; set; } = "VoiceGardenSAPI";
    public bool ShowEdgeVoices { get; set; } = false;
    public bool ShowNarratorVoices { get; set; } = false;
    public bool ShowAdvancedSection { get; set; } = true;
    public bool DefaultSherpaEnabled { get; set; } = true;
    public bool DefaultAzureEnabled { get; set; } = false;

    public static BrandingConfig Load(string? path = null)
    {
        path ??= Path.Combine(System.AppContext.BaseDirectory, "branding.json");
        if (!File.Exists(path))
            return new BrandingConfig();

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<BrandingConfig>(json);
            return config ?? new BrandingConfig();
        }
        catch
        {
            return new BrandingConfig();
        }
    }
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
