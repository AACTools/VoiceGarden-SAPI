using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VoiceGarden.UI.Models;
using VoiceGarden.UI.ViewModels;

namespace VoiceGarden.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);

        FlowDirection = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? (Avalonia.Media.FlowDirection)1
            : (Avalonia.Media.FlowDirection)0;

        // Set window icon from embedded ICO resource
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://VoiceGarden.UI/Assets/app.ico"));
            Icon = new WindowIcon(stream);
        }
        catch { /* don't crash if icon missing */ }
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private async void VerifyCredentials_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string engineId) return;

        if (DataContext is not MainViewModel vm) return;
        var engine = vm.CloudEngines.FirstOrDefault(e => e.Id == engineId);
        if (engine == null) return;

        engine.VerificationStatus = "Checking...";
        btn.IsEnabled = false;

        try
        {
            await Task.Run(() =>
            {
                var cap = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(engineId);
                var key = engine.ApiKey ?? "";
                var region = engine.Region ?? "";

                var creds = BuildCreds(engineId, key, region);
                if (creds == null)
                {
                    engine.VerificationStatus = "✗ Unknown engine";
                    return;
                }

                using var client = new RustTtsWrapper.TtsClient(engineId, creds);
                var voices = client.GetVoices();
                if (voices.Count > 0)
                    engine.VerificationStatus = $"✓ Valid ({voices.Count} voices)";
                else
                    engine.VerificationStatus = "✗ No voices returned — check API key and enable TTS";
            });
        }
        catch (Exception ex)
        {
            engine.VerificationStatus = $"✗ {ex.Message}";
        }
        finally
        {
            btn.IsEnabled = engine.HasKey;
        }
    }

    private static Dictionary<string, string>? BuildCreds(string engine, string key, string region)
    {
        return engine.ToLowerInvariant() switch
        {
            "azure" => new() { { "subscriptionKey", key }, { "region", region } },
            "openai" or "elevenlabs" or "google" or "cartesia" or "deepgram" or
            "fishaudio" or "hume" or "mistral" or "murf" or "resemble" or
            "unrealspeech" or "upliftai" or "xai" or "modelslab" =>
                new() { { "apiKey", key } },
            "polly" => new() { { "accessKeyId", key }, { "secretAccessKey", region }, { "region", "us-east-1" } },
            "watson" => new() { { "apiKey", key }, { "region", region }, { "instanceId", "" } },
            "playht" => new() { { "apiKey", key }, { "userId", region } },
            "witai" => new() { { "token", key } },
            _ => null,
        };
    }
}
