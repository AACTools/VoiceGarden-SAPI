using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceGarden.UI.Localization;
using VoiceGarden.UI.Models;
using VoiceGarden.UI.Services;

namespace VoiceGarden.UI.ViewModels;

/// <summary>
/// One credential form row in the Credentials tab. Wraps the persistent
/// CloudEngineSetting and derives field labels from the engine's declared
/// credential keys. Carries its own verify command so XAML never needs
/// cross-tree bindings.
/// </summary>
public partial class CredentialsForm : ObservableObject
{
    public CloudEngineSetting Setting { get; }

    /// <summary>Invoked after a verification completes (parent refreshes key badges).</summary>
    public Action? Verified { get; set; }

    public CredentialsForm(CloudEngineSetting setting)
    {
        Setting = setting;
        var def = EngineDefinition.DiscoverAll()
            .FirstOrDefault(d => d.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase));
        var keys = def?.CredentialKeys ?? Array.Empty<string>();

        PrimaryLabel = EngineCatalogItem.CredentialKeyLabel(
            keys.FirstOrDefault(k => k is "apiKey" or "subscriptionKey" or "accessKeyId" or "token") ?? "apiKey");

        var secondaryKey = keys.FirstOrDefault(k => k is "region" or "userId" or "secretAccessKey");
        SecondaryLabel = secondaryKey != null ? EngineCatalogItem.CredentialKeyLabel(secondaryKey) : "";
    }

    public string Id => Setting.Id;
    public string DisplayName => Setting.DisplayName;
    public string PrimaryLabel { get; }
    public string SecondaryLabel { get; }
    public bool NeedsSecondary => SecondaryLabel.Length > 0;

    [ObservableProperty] private bool isVerifying;

    [RelayCommand]
    private async Task Verify()
    {
        if (IsVerifying) return;

        if (string.IsNullOrWhiteSpace(Setting.ApiKey))
        {
            Setting.VerificationStatus = Loc.GetString("EnterApiKey");
            return;
        }

        IsVerifying = true;
        Setting.VerificationStatus = Loc.GetString("Validating");

        try
        {
            var engineId = Setting.Id;
            var key = Setting.ApiKey ?? "";
            var region = Setting.Region ?? "";

            var result = await Task.Run(() =>
            {
                var creds = TtsCredentialBuilder.Build(engineId, key, region);
                if (creds == null)
                    return (ok: false, count: 0, error: Loc.GetString("UnknownEngine"));

                try
                {
                    using var client = new RustTtsWrapper.TtsClient(engineId, creds);
                    var voices = client.GetVoices();
                    return voices.Count > 0
                        ? (ok: true, count: voices.Count, error: "")
                        : (ok: false, count: 0, error: Loc.GetString("VerifyNoVoices"));
                }
                catch (Exception ex)
                {
                    return (ok: false, count: 0, error: ex.Message);
                }
            });

            Setting.VerificationStatus = result.ok
                ? $"✓ {Loc.GetString("ValidResult", result.count)}"
                : $"✗ {result.error}";
        }
        finally
        {
            IsVerifying = false;
            Verified?.Invoke();
        }
    }
}

/// <summary>
/// Tab 2 — credential forms for the engines selected in Tab 1 that need
/// credentials, with per-engine validation.
/// </summary>
public partial class CredentialsViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public CredentialsViewModel(MainViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<CredentialsForm> Forms { get; } = new();

    [ObservableProperty] private string statusText = "";

    /// <summary>Rebuild the form list from the engines currently selected in Tab 1.</summary>
    public void Refresh()
    {
        var selected = _owner.Engines.SelectedCredsEngines()
            .Select(e => e.CloudSetting)
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();

        // Keep existing forms (verification state, in-progress edits) where possible.
        var existing = Forms.ToList();
        Forms.Clear();
        foreach (var setting in selected)
        {
            var form = existing.FirstOrDefault(f => f.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase))
                       ?? new CredentialsForm(setting);
            form.Verified = RefreshKeyHints;
            Forms.Add(form);
        }

        StatusText = Forms.Count == 0
            ? Loc.GetString("CredentialsNoneNeeded")
            : Loc.GetString("CredentialsIntro");
    }

    private void RefreshKeyHints() => _owner.Engines.RefreshKeyHints();

    [RelayCommand]
    private void GoToEngines() => _owner.GoToEngines();

    [RelayCommand]
    private void GoToVoices() => _owner.GoToVoices();
}
