using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceGarden.UI.Models;
using VoiceGarden.UI.Services;

namespace VoiceGarden.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly BrandingConfig _branding;

    public MainViewModel()
    {
        _branding = BrandingConfig.Load();
        AppName = _branding.AppName;
        ShowAdvanced = false; // Hidden by default

        // Load settings from registry
        SherpaEnabled = !RegistryService.GetFlag("NoSherpaVoices", !_branding.DefaultSherpaEnabled);
        EdgeEnabled = !RegistryService.GetFlag("NoEdgeVoices");
        NarratorEnabled = !RegistryService.GetFlag("NoNarratorVoices");
        LogLevelIndex = RegistryService.GetDword("LogLevel", 0);

        // Load cloud engines
        foreach (var def in EngineDefinition.All)
        {
            var setting = new CloudEngineSetting
            {
                Id = def.Id,
                DisplayName = def.DisplayName,
                NeedsRegion = def.NeedsRegion,
                Enabled = !RegistryService.GetFlag($"No{Cap(def.Id)}Voices"),
            };

            // Pre-fill Azure key from legacy registry
            if (def.Id == "azure")
            {
                setting.ApiKey = RegistryService.GetString("AzureVoiceKey") ?? "";
                setting.Region = RegistryService.GetString("AzureVoiceRegion") ?? "eastus";
            }

            setting.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CloudEngineSetting.Enabled))
                {
                    var eng = (CloudEngineSetting)s!;
                    RegistryService.SetFlag(eng.NoVoicesRegName, !eng.Enabled);

                    // Save Azure key to legacy location
                    if (eng.Id == "azure" && !string.IsNullOrEmpty(eng.ApiKey))
                    {
                        RegistryService.SetString("AzureVoiceKey", eng.ApiKey);
                        RegistryService.SetString("AzureVoiceRegion", eng.Region);
                    }
                }
            };

            CloudEngines.Add(setting);
        }

        // Count installed SherpaOnnx models
        UpdateSherpaModelCount();

        // Check adapter installation status
        RefreshInstallStatus();
    }

    [ObservableProperty] private string appName = "VoiceGarden";
    [ObservableProperty] private bool showAdvanced = true;
    [ObservableProperty] private bool sherpaEnabled = true;
    [ObservableProperty] private bool edgeEnabled = false;
    [ObservableProperty] private bool narratorEnabled = false;
    [ObservableProperty] private int logLevelIndex = 0;
    [ObservableProperty] private string status64Bit = "Checking...";
    [ObservableProperty] private string status32Bit = "Checking...";
    [ObservableProperty] private bool is64Installed = false;
    [ObservableProperty] private bool is32Installed = false;
    [ObservableProperty] private string install64Text = "Install";
    [ObservableProperty] private string install32Text = "Install";
    [ObservableProperty] private string sherpaModelSummary = "";
    [ObservableProperty] private bool isAboutVisible = false;
    public string AboutText =>
        $"VoiceGarden v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}\n\n" +
        "SAPI Voice Adapter Configuration Tool\n\n" +
        $"DotNetTtsWrapper: {typeof(DotNetTtsWrapper.Models.TtsFactory).Assembly.GetName().Version}\n" +
        "Engines: Azure, OpenAI, ElevenLabs, Google, Polly, Cartesia, Deepgram,\n" +
        "SherpaOnnx (offline), Watson, PlayHT, Wit.ai, Gemini, and more\n\n" +
        "https://github.com/AACTools/VoiceGarden-SAPI";

    public ObservableCollection<CloudEngineSetting> CloudEngines { get; } = new();

    public string AdvancedToggleText => ShowAdvanced ? "▼ Hide Advanced" : "▶ Show Advanced";

    partial void OnSherpaEnabledChanged(bool value) => RegistryService.SetFlag("NoSherpaVoices", !value);
    partial void OnEdgeEnabledChanged(bool value) => RegistryService.SetFlag("NoEdgeVoices", !value);
    partial void OnNarratorEnabledChanged(bool value) => RegistryService.SetFlag("NoNarratorVoices", !value);
    partial void OnLogLevelIndexChanged(int value) => RegistryService.SetDword("LogLevel", value);
    partial void OnShowAdvancedChanged(bool value) => OnPropertyChanged(nameof(AdvancedToggleText));

    [ObservableProperty] private bool isVoiceConfigVisible = false;
    [ObservableProperty] private bool isSherpaManagerVisible = false;
    [ObservableProperty] private bool isEngineConfigVisible = false;

    public bool IsMainViewVisible => !IsVoiceConfigVisible && !IsSherpaManagerVisible && !IsEngineConfigVisible;

    public VoiceConfigViewModel VoiceConfig { get; } = new();
    public SherpaModelsViewModel SherpaModels { get; } = new();

    [RelayCommand]
    private void OpenVoiceConfig()
    {
        // Find first enabled cloud engine and pre-fill its key
        var firstEnabled = CloudEngines.FirstOrDefault(e => e.Enabled);
        if (firstEnabled != null)
        {
            VoiceConfig.Initialize(firstEnabled.Id, firstEnabled.ApiKey, firstEnabled.Region);
        }
        else
        {
            VoiceConfig.Initialize("azure", "", "eastus");
        }
        IsVoiceConfigVisible = true;
        OnPropertyChanged(nameof(IsMainViewVisible));
    }

    [RelayCommand]
    private void OpenEngineConfig()
    {
        IsEngineConfigVisible = true;
        OnPropertyChanged(nameof(IsMainViewVisible));
    }

    [RelayCommand]
    private void BackToMain()
    {
        IsVoiceConfigVisible = false;
        IsSherpaManagerVisible = false;
        IsEngineConfigVisible = false;
        IsAboutVisible = false;
        OnPropertyChanged(nameof(IsMainViewVisible));
    }

    private void UpdateSherpaModelCount()
    {
        var modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NaturalVoiceSAPIAdapter", "models");
        if (Directory.Exists(modelsDir))
        {
            var count = Directory.GetDirectories(modelsDir).Length;
            SherpaModelSummary = $"{count} model(s) installed";
        }
        else
        {
            SherpaModelSummary = "No models installed";
        }
    }

    private void RefreshInstallStatus()
    {
        is64Installed = ComRegistrationService.IsRegistered(true);
        is32Installed = ComRegistrationService.IsRegistered(false);
        Status64Bit = is64Installed ? "✓ 64-bit adapter registered" : "64-bit: not registered";
        Status32Bit = is32Installed ? "✓ 32-bit adapter registered" : "32-bit: not registered";
        Install64Text = is64Installed ? "Re-register" : "Register";
        Install32Text = is32Installed ? "Re-register" : "Register";
    }

    [RelayCommand]
    private void Install64()
    {
        ComRegistrationService.Register(true);
        RefreshInstallStatus();
    }

    [RelayCommand]
    private void Uninstall64()
    {
        ComRegistrationService.Unregister(true);
        RefreshInstallStatus();
    }

    [RelayCommand]
    private void Install32()
    {
        ComRegistrationService.Register(false);
        RefreshInstallStatus();
    }

    [RelayCommand]
    private void Uninstall32()
    {
        ComRegistrationService.Unregister(false);
        RefreshInstallStatus();
    }

    [RelayCommand]
    private void OpenSherpaManager()
    {
        IsSherpaManagerVisible = true;
        OnPropertyChanged(nameof(IsMainViewVisible));
        _ = SherpaModels.LoadCatalogCommand.ExecuteAsync(null);
    }


    [RelayCommand]
    private void OpenLogs()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NaturalVoiceSAPIAdapter");
        if (Directory.Exists(logDir))
            Process.Start("explorer.exe", $"\"{logDir}\"");
    }

    [RelayCommand]
    private void ShowAbout()
    {
        IsAboutVisible = true;
        OnPropertyChanged(nameof(IsMainViewVisible));
    }

    private static string Cap(string s) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s);
}
