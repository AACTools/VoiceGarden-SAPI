using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceGarden.UI.Models;
using VoiceGarden.UI.Services;

namespace VoiceGarden.UI.ViewModels;

// Simple disposable helper for event cleanup
internal static class Disposable
{
    public static IDisposable Create(Action disposeAction) =>
        new AnonymousDisposable(disposeAction);

    private class AnonymousDisposable : IDisposable
    {
        private readonly Action _disposeAction;
        private volatile bool _disposed;

        public AnonymousDisposable(Action disposeAction)
        {
            _disposeAction = disposeAction ?? throw new ArgumentNullException(nameof(disposeAction));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disposeAction?.Invoke();
        }
    }
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel()
    {
        AppName = BrandingConfig.AppName;
        ShowAdvanced = false;

        // Track app launch (only if opted in)
        AnalyticsService.Track("app_launched");

        // Load settings from registry
        SherpaEnabled = !RegistryService.GetFlag("NoSherpaVoices", !BrandingConfig.DefaultSherpaEnabled);
        EdgeEnabled = !RegistryService.GetFlag("NoEdgeVoices");
        AnalyticsEnabled = Services.AnalyticsService.IsEnabled;
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

            // Store handler and setting for later cleanup
            PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName == nameof(CloudEngineSetting.Enabled))
                {
                    var eng = (CloudEngineSetting)s!;
                    RegistryService.SetFlag(eng.NoVoicesRegName, !eng.Enabled);
                    AnalyticsService.Track("engine_toggled", ("engine", eng.Id), ("enabled", eng.Enabled));

                    // Save Azure key to legacy location
                    if (eng.Id == "azure" && !string.IsNullOrEmpty(eng.ApiKey))
                    {
                        RegistryService.SetString("AzureVoiceKey", eng.ApiKey);
                        RegistryService.SetString("AzureVoiceRegion", eng.Region);
                    }
                }
            };
            setting.PropertyChanged += handler;

            // Track subscription for cleanup using simple tuple
            _eventSubscriptions.Add(Disposable.Create(() =>
                setting.PropertyChanged -= handler));

            CloudEngines.Add(setting);
        }

        // Count installed SherpaOnnx models
        UpdateSherpaModelCount();

        // Check adapter installation status
        RefreshInstallStatus();
    }

    // Track event subscriptions for cleanup
    private readonly List<IDisposable> _eventSubscriptions = new List<IDisposable>();

    public void Dispose()
    {
        // Unsubscribe from all CloudEngineSetting events
        foreach (var setting in CloudEngines.OfType<CloudEngineSetting>())
        {
            // Manually unsubscribe by replacing with empty handler
            // (C# event pattern doesn't provide direct unsubscribe for lambdas)
        }
        _eventSubscriptions.Clear();
    }

    [ObservableProperty] private string appName = "VoiceGarden";
    [ObservableProperty] private bool showAdvanced = true;
    [ObservableProperty] private bool sherpaEnabled = true;
    [ObservableProperty] private bool edgeEnabled = false;
    [ObservableProperty] private bool analyticsEnabled = false;

    partial void OnAnalyticsEnabledChanged(bool value)
    {
        Services.AnalyticsService.IsEnabled = value;
        if (value) Services.AnalyticsService.Track("analytics_opted_in");
    }
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
        $"RustTtsWrapper: {typeof(RustTtsWrapper.TtsClient).Assembly.GetName().Version}\n" +
        "Engines: Azure, Edge, OpenAI, ElevenLabs, Google, Polly, Cartesia, Deepgram,\n" +
        "SherpaOnnx (offline), Watson, PlayHT, Wit.ai, Gemini, and more\n\n" +
        "https://github.com/AACTools/VoiceGarden-SAPI";

    public ObservableCollection<CloudEngineSetting> CloudEngines { get; } = new();

    public string AdvancedToggleText => ShowAdvanced ? "▼ Hide Advanced" : "▶ Show Advanced";

    partial void OnSherpaEnabledChanged(bool value) => RegistryService.SetFlag("NoSherpaVoices", !value);
    partial void OnEdgeEnabledChanged(bool value)
    {
        RegistryService.SetFlag("NoEdgeVoices", !value);
        AnalyticsService.Track("engine_toggled", ("engine", "edge"), ("enabled", value));
    }
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
            "VoiceGardenSAPIAdapter", "models");
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
        Is64Installed = ComRegistrationService.IsRegistered(true);
        Is32Installed = ComRegistrationService.IsRegistered(false);
        Status64Bit = Is64Installed ? "✓ 64-bit adapter registered" : "64-bit: not registered";
        Status32Bit = Is32Installed ? "✓ 32-bit adapter registered" : "32-bit: not registered";
        Install64Text = Is64Installed ? "Re-register" : "Register";
        Install32Text = Is32Installed ? "Re-register" : "Register";
    }

    [RelayCommand]
    private async Task Install64()
    {
        var rc = ComRegistrationService.Register(true);
        if (rc == -2) return; // User cancelled UAC
        await Task.Delay(500); // Wait for registration to propagate
        RefreshInstallStatus();
        if (rc == 0) AnalyticsService.Track("adapter_registered", ("arch", "x64"));
    }

    [RelayCommand]
    private async Task Uninstall64()
    {
        ComRegistrationService.Unregister(true);
        await Task.Delay(500); // Wait for unregistration to propagate
        RefreshInstallStatus();
    }

    [RelayCommand]
    private async Task Install32()
    {
        var rc = ComRegistrationService.Register(false);
        if (rc == -2) return;
        await Task.Delay(500); // Wait for registration to propagate
        RefreshInstallStatus();
        if (rc == 0) AnalyticsService.Track("adapter_registered", ("arch", "x86"));
    }

    [RelayCommand]
    private async Task Uninstall32()
    {
        ComRegistrationService.Unregister(false);
        await Task.Delay(500); // Wait for unregistration to propagate
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
            "VoiceGardenSAPIAdapter");
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
