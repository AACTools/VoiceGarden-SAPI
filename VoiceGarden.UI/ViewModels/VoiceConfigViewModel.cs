using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceGarden.UI.Services;

namespace VoiceGarden.UI.ViewModels;

public partial class VoiceItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Language { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Provider { get; set; } = "";

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isInstalled;
}

public partial class VoiceConfigViewModel : ObservableObject
{
    private string _currentEngine = "azure";
    private string _currentKey = "";
    private string _currentRegion = "";

    [ObservableProperty] private string searchFilter = "";
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isValidated;
    [ObservableProperty] private string validationResult = "";
    [ObservableProperty] private int totalVoices;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private int installedCount;

    public ObservableCollection<VoiceItem> AllVoices { get; } = new();
    public ObservableCollection<VoiceItem> FilteredVoices { get; } = new();

    public string[] AvailableEngines { get; } = { "azure", "openai", "elevenlabs", "google", "polly", "cartesia", "deepgram" };

    public string CurrentEngine
    {
        get => _currentEngine;
        set { _currentEngine = value; OnPropertyChanged(); _ = RefreshInstalledStatus(); }
    }

    public string CurrentKey
    {
        get => _currentKey;
        set { _currentKey = value; OnPropertyChanged(); IsValidated = false; }
    }

    public string CurrentRegion
    {
        get => _currentRegion;
        set { _currentRegion = value; OnPropertyChanged(); }
    }

    public bool NeedsRegion => CurrentEngine is "azure" or "polly";

    partial void OnSearchFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task FetchVoices()
    {
        if (string.IsNullOrWhiteSpace(CurrentKey))
        {
            StatusText = "Enter an API key first";
            return;
        }

        IsLoading = true;
        StatusText = $"Fetching {CurrentEngine} voices...";
        AllVoices.Clear();
        FilteredVoices.Clear();

        try
        {
            var creds = BuildRustCredentials();
            if (creds == null)
            {
                StatusText = $"Unknown engine: {CurrentEngine}";
                IsLoading = false;
                return;
            }

            using var client = new RustTtsWrapper.TtsClient(CurrentEngine, creds);
            var voices = client.GetVoices();
            TotalVoices = voices.Count;

            foreach (var v in voices)
            {
                var item = new VoiceItem
                {
                    Id = v.Id ?? "",
                    Name = v.Name ?? v.Id ?? "",
                    Language = string.IsNullOrEmpty(v.Language) ? "en-US" : v.Language,
                    Gender = v.Gender ?? "Unknown",
                    Provider = v.Engine ?? CurrentEngine,
                };
                AllVoices.Add(item);
            }

            ApplyFilter();
            await RefreshInstalledStatus();
            StatusText = $"Found {TotalVoices} voices";
            IsValidated = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ValidateKey()
    {
        if (string.IsNullOrWhiteSpace(CurrentKey))
        {
            ValidationResult = "Enter an API key";
            return;
        }

        IsLoading = true;
        ValidationResult = "Validating...";

        try
        {
            var creds = BuildRustCredentials();
            if (creds == null)
            {
                ValidationResult = "Unknown engine";
                return;
            }

            // Try to list voices as credential validation
            using var client = new RustTtsWrapper.TtsClient(CurrentEngine, creds);
            var voices = client.GetVoices();
            ValidationResult = $"Valid ({voices.Count} voices)";
            IsValidated = true;
        }
        catch (RustTtsWrapper.TtsException ex)
        {
            ValidationResult = $"Invalid: {ex.Message}";
            IsValidated = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void PromoteSelected()
    {
        var selected = AllVoices.Where(v => v.IsSelected && !v.IsInstalled).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Select voices to install first";
            return;
        }

        int promoted = 0, failed = 0;
        foreach (var voice in selected)
        {
            var rc = VoicePromotionService.PromoteElevated(CurrentEngine, voice.Id, CurrentKey, CurrentRegion);
            if (rc == 0)
            {
                voice.IsInstalled = true;
                promoted++;
            }
            else
            {
                failed++;
            }
        }

        _ = RefreshInstalledStatus();
        StatusText = failed == 0
            ? $"Installed {promoted} voice(s) to HKLM"
            : $"Installed {promoted}, failed {failed}";
    }

    [RelayCommand]
    private void UnpromoteSelected()
    {
        var selected = AllVoices.Where(v => v.IsSelected && v.IsInstalled).ToList();
        foreach (var voice in selected)
        {
            var tokenName = $"Cloud-{CurrentEngine}-{voice.Id}".Replace("/", "_").Replace("\\", "_");
            VoicePromotionService.UnpromoteElevated(tokenName);
            voice.IsInstalled = false;
        }
        _ = RefreshInstalledStatus();
        StatusText = $"Removed {selected.Count} voice(s)";
    }

    [RelayCommand]
    private async Task PreviewVoice(VoiceItem voice)
    {
        if (string.IsNullOrWhiteSpace(CurrentKey))
        {
            StatusText = "Enter an API key first";
            return;
        }

        StatusText = $"Previewing {voice.Name}...";
        try
        {
            // Use rust-tts-wrapper for cloud voice preview
            var creds = BuildRustCredentials();
            if (creds == null) return;

            using var client = new RustTtsWrapper.TtsClient(CurrentEngine, creds);
            client.SetVoice(voice.Id);

            var audioData = client.SynthToBytes($"Hello, my name is {voice.Name}.");
            if (audioData.Length > 0)
            {
                // Rust returns raw PCM16 mono — wrap in WAV header for SoundPlayer
                var wavData = WrapPcmInWav(audioData, 24000);
                var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voicegarden_preview_{Guid.NewGuid():N}.wav");
                await System.IO.File.WriteAllBytesAsync(tempFile, wavData);
                _ = Task.Run(() =>
                {
                    try { using var player = new System.Media.SoundPlayer(tempFile); player.PlaySync(); }
                    catch { }
                    finally { try { System.IO.File.Delete(tempFile); } catch { }
                    }
                });
                StatusText = $"Previewing {voice.Name}";
            }
            else
            {
                StatusText = "No audio generated";
            }
        }
        catch (RustTtsWrapper.TtsException ex)
        {
            StatusText = $"Preview failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var v in FilteredVoices) v.IsSelected = true;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var v in AllVoices) v.IsSelected = false;
        UpdateSelectedCount();
    }

    private Dictionary<string, string>? BuildRustCredentials()
    {
        return CurrentEngine.ToLowerInvariant() switch
        {
            "azure" => new() { { "subscriptionKey", CurrentKey }, { "region", CurrentRegion } },
            "openai" or "elevenlabs" or "google" or "cartesia" or "deepgram" or
            "fishaudio" or "hume" or "mistral" or "murf" or "resemble" or
            "unrealspeech" or "upliftai" or "xai" or "modelslab" =>
                new() { { "apiKey", CurrentKey } },
            "watson" => new() { { "apiKey", CurrentKey }, { "region", CurrentRegion } },
            "playht" => new() { { "apiKey", CurrentKey }, { "userId", CurrentRegion } },
            "witai" => new() { { "token", CurrentKey } },
            _ => null,
        };
    }

    /// <summary>
    /// Wrap raw PCM16 mono samples in a WAV header so SoundPlayer can play them.
    /// Rust's SynthToBytes returns raw PCM16, not WAV.
    /// </summary>
    private static byte[] WrapPcmInWav(byte[] pcm, int sampleRate)
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        short channels = 1;
        short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int dataLen = pcm.Length;
        int riffLen = 36 + dataLen;

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(riffLen);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // PCM chunk size
        bw.Write((short)1); // PCM format
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataLen);
        bw.Write(pcm);
        return ms.ToArray();
    }

    private void ApplyFilter()
    {
        FilteredVoices.Clear();
        var filter = SearchFilter?.Trim().ToLowerInvariant() ?? "";

        foreach (var v in AllVoices)
        {
            if (string.IsNullOrEmpty(filter) ||
                v.Name.ToLowerInvariant().Contains(filter) ||
                v.Id.ToLowerInvariant().Contains(filter) ||
                v.Language.ToLowerInvariant().Contains(filter))
            {
                FilteredVoices.Add(v);
            }
        }
        StatusText = string.IsNullOrEmpty(filter)
            ? $"{TotalVoices} voices"
            : $"Showing {FilteredVoices.Count} of {TotalVoices} voices";
    }

    private async Task RefreshInstalledStatus()
    {
        var promoted = VoicePromotionService.ListPromoted();
        InstalledCount = 0;
        foreach (var v in AllVoices)
        {
            var tokenName = $"Cloud-{CurrentEngine}-{v.Id}".Replace("/", "_").Replace("\\", "_");
            v.IsInstalled = promoted.Any(p => p.TokenName.Equals(tokenName, StringComparison.OrdinalIgnoreCase));
            if (v.IsInstalled) InstalledCount++;
        }
        UpdateSelectedCount();
        await Task.CompletedTask;
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = AllVoices.Count(v => v.IsSelected);
    }

    public void Initialize(string engine, string key, string region)
    {
        CurrentEngine = engine;
        CurrentKey = key;
        CurrentRegion = region;
    }
}
