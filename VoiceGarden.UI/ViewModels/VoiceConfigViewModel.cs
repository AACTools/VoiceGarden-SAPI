using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetTtsWrapper.Models;
using DotNetTtsWrapper.Engines;
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
            var creds = BuildCredentials();
            var client = TtsFactory.CreateClient(CurrentEngine, creds);
            if (client == null)
            {
                StatusText = $"Unknown engine: {CurrentEngine}";
                IsLoading = false;
                return;
            }

            var voices = await client.GetVoicesAsync();
            TotalVoices = voices.Count;

            foreach (var v in voices)
            {
                var item = new VoiceItem
                {
                    Id = v.Id ?? "",
                    Name = v.Name ?? v.Id ?? "",
                    Language = v.LanguageCodes?.FirstOrDefault()?.Bcp47 ?? "en-US",
                    Gender = v.Gender.ToString(),
                    Provider = v.Provider ?? CurrentEngine,
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
            var creds = BuildCredentials();
            var client = TtsFactory.CreateClient(CurrentEngine, creds);
            if (client == null)
            {
                ValidationResult = "Unknown engine";
                return;
            }

            // Try CheckCredentialsAsync first
            var result = await client.CheckCredentialsAsync();
            if (result.IsValid)
            {
                ValidationResult = $"Valid ({result.AvailableVoiceCount} voices)";
                IsValidated = true;
                return;
            }

            // Fall back to synthesis test
            try
            {
                var synthResult = await client.SynthToBytesAsync("test");
                if (synthResult?.AudioData?.Length > 0)
                {
                    ValidationResult = "Valid (synthesis test passed)";
                    IsValidated = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                ValidationResult = $"Invalid: {ex.Message}";
            }

            IsValidated = false;
        }
        catch (Exception ex)
        {
            ValidationResult = $"Error: {ex.Message}";
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

    private ITtsCredentials? BuildCredentials()
    {
        return CurrentEngine.ToLowerInvariant() switch
        {
            "azure" => new AzureCredentials { SubscriptionKey = CurrentKey, Region = CurrentRegion },
            "openai" => new OpenAICredentials { ApiKey = CurrentKey },
            "elevenlabs" => new ElevenLabsCredentials { ApiKey = CurrentKey },
            "google" => new GoogleCredentials { ApiKey = CurrentKey },
            "polly" => new PollyCredentials { AccessKeyId = CurrentKey, SecretAccessKey = "", Region = CurrentRegion },
            "cartesia" => new CartesiaCredentials { ApiKey = CurrentKey },
            "deepgram" => new DeepgramCredentials { ApiKey = CurrentKey },
            _ => null,
        };
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
            ? $"Showing {TotalVoices} voices"
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
