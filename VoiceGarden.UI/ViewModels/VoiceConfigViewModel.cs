using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceGarden.UI.Localization;
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

    [ObservableProperty] private string searchFilter = "";
    [ObservableProperty] private string statusText = Loc.GetString("Ready");
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isValidated;
    [ObservableProperty] private string validationResult = "";
    [ObservableProperty] private int totalVoices;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private int installedCount;

    public ObservableCollection<VoiceItem> AllVoices { get; } = new();
    public ObservableCollection<VoiceItem> FilteredVoices { get; } = new();

    /// <summary>Only engines enabled (ticked) on the main page.</summary>
    [ObservableProperty] private string[] availableEngines = Array.Empty<string>();

    public string CurrentEngine
    {
        get => _currentEngine;
        set {
            _currentEngine = value;
            OnPropertyChanged();
            IsValidated = false;
            _ = RefreshInstalledStatus();
        }
    }

    private string GetKey() => GetSavedKey(_currentEngine);
    private string GetRegion() => GetSavedRegion(_currentEngine);

    private static string GetSavedKey(string engine)
    {
        var cap = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(engine);
        return Services.RegistryService.GetString($"{cap}VoiceKey")
            ?? (engine == "azure" ? Services.RegistryService.GetString("AzureVoiceKey") : null) ?? "";
    }

    private static string GetSavedRegion(string engine)
    {
        var cap = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(engine);
        return Services.RegistryService.GetString($"{cap}VoiceRegion")
            ?? (engine == "azure" ? Services.RegistryService.GetString("AzureVoiceRegion") : null) ?? "";
    }

    partial void OnSearchFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task FetchVoices()
    {
        if (string.IsNullOrWhiteSpace(GetKey()))
        {
            StatusText = Loc.GetString("EnterApiKeyFirst");
            return;
        }

        IsLoading = true;
        StatusText = Loc.GetString("FetchingVoices", CurrentEngine);
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

            if (voices.Count == 0)
            {
                StatusText = "No voices returned. Check your API key and try Validate first.";
                IsValidated = false;
                return;
            }

            foreach (var v in voices)
            {
                AllVoices.Add(new VoiceItem
                {
                    Id = v.Id ?? "",
                    Name = v.Name ?? v.Id ?? "",
                    Language = string.IsNullOrEmpty(v.Language) ? "en-US" : v.Language,
                    Gender = v.Gender ?? "Unknown",
                    Provider = v.Engine ?? CurrentEngine,
                });
            }

            ApplyFilter();
            await RefreshInstalledStatus();
            StatusText = Loc.GetString("FoundVoices", TotalVoices);
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
        if (string.IsNullOrWhiteSpace(GetKey()))
        {
            ValidationResult = Loc.GetString("EnterApiKey");
            return;
        }

        IsLoading = true;
        ValidationResult = Loc.GetString("Validating");

        try
        {
            var creds = BuildRustCredentials();
            if (creds == null)
            {
                ValidationResult = "Unknown engine";
                return;
            }

            using var client = new RustTtsWrapper.TtsClient(CurrentEngine, creds);
            var voices = client.GetVoices();
            if (voices.Count == 0)
            {
                ValidationResult = "Key accepted but no voices returned. The key may be invalid or have no TTS access.";
                IsValidated = false;
            }
            else
            {
                ValidationResult = Loc.GetString("ValidResult", voices.Count);
                IsValidated = true;
            }
        }
        catch (RustTtsWrapper.TtsException ex)
        {
            ValidationResult = Loc.GetString("InvalidResult", ex.Message);
            IsValidated = false;
        }
        catch (Exception ex)
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
            StatusText = Loc.GetString("SelectVoicesFirst");
            return;
        }

        int promoted = 0, failed = 0;
        foreach (var voice in selected)
        {
            var rc = VoicePromotionService.PromoteElevated(CurrentEngine, voice.Id, GetKey(), GetRegion());
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
            ? Loc.GetString("InstalledVoicesHKLM", promoted)
            : Loc.GetString("InstalledModelsFailed", promoted, failed);
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
        StatusText = Loc.GetString("RemovedVoices", selected.Count);
    }

    [RelayCommand]
    private async Task PreviewVoice(VoiceItem voice)
    {
        if (string.IsNullOrWhiteSpace(GetKey()))
        {
            StatusText = Loc.GetString("EnterApiKeyFirst");
            return;
        }

        StatusText = Loc.GetString("PreviewingVoice", voice.Name);
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
                StatusText = Loc.GetString("PreviewingVoice", voice.Name);
            }
            else
            {
                StatusText = Loc.GetString("NoAudio");
            }
        }
        catch (RustTtsWrapper.TtsException ex)
        {
            StatusText = Loc.GetString("PreviewFailed", ex.Message);
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

    /// <summary>
    /// Build credentials dynamically using the engine's credential_keys
    /// from the Rust wrapper. No hardcoded engine-specific logic.
    /// </summary>
    private Dictionary<string, string>? BuildRustCredentials()
    {
        var engine = Models.EngineDefinition.DiscoverAll()
            .FirstOrDefault(e => e.Id.Equals(CurrentEngine, StringComparison.OrdinalIgnoreCase));
        if (engine == null) return null;

        var key = GetKey();
        var region = GetRegion();
        var creds = new Dictionary<string, string>();

        foreach (var credKey in engine.CredentialKeys)
        {
            var value = credKey switch
            {
                "apiKey" or "subscriptionKey" or "accessKeyId" or "token" => key,
                "region" or "userId" => region,
                "secretAccessKey" => region,
                "instanceId" => "",
                _ => key, // Default: treat as the primary key
            };
            creds[credKey] = value;
        }

        // Polly needs a default region if not specified
        if (CurrentEngine.Equals("polly", StringComparison.OrdinalIgnoreCase) && !creds.ContainsKey("region"))
            creds["region"] = "us-east-1";

        return creds.Count > 0 ? creds : null;
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

    public void Initialize(string engine)
    {
        CurrentEngine = engine;
        // Credentials now read from registry via GetKey()/GetRegion()
    }
}

