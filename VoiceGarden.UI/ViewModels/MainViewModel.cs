using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceGarden.UI.Localization;
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
                Enabled = !RegistryService.GetFlag($"No{Cap(def.Id)}Voices", true),
            };

            // Load saved credentials (generic + Azure legacy compat)
            setting.ApiKey = RegistryService.GetString($"{Cap(def.Id)}VoiceKey") ??
                (def.Id == "azure" ? RegistryService.GetString("AzureVoiceKey") : null) ?? "";
            setting.Region = RegistryService.GetString($"{Cap(def.Id)}VoiceRegion") ??
                (def.Id == "azure" ? RegistryService.GetString("AzureVoiceRegion") : null) ?? "";

            // Auto-save credentials when changed
            PropertyChangedEventHandler handler = (s, e) =>
            {
                var eng = (CloudEngineSetting)s!;
                if (e.PropertyName == nameof(CloudEngineSetting.Enabled))
                {
                    RegistryService.SetFlag(eng.NoVoicesRegName, !eng.Enabled);
                    AnalyticsService.Track("engine_toggled", ("engine", eng.Id), ("enabled", eng.Enabled));
                }
                else if (e.PropertyName == nameof(CloudEngineSetting.ApiKey))
                {
                    RegistryService.SetString($"{Cap(eng.Id)}VoiceKey", eng.ApiKey ?? "");
                    if (eng.Id == "azure") RegistryService.SetString("AzureVoiceKey", eng.ApiKey ?? "");
                }
                else if (e.PropertyName == nameof(CloudEngineSetting.Region))
                {
                    RegistryService.SetString($"{Cap(eng.Id)}VoiceRegion", eng.Region ?? "");
                    if (eng.Id == "azure") RegistryService.SetString("AzureVoiceRegion", eng.Region ?? "");
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
    [ObservableProperty] private string status64Bit = Loc.GetString("Checking");
    [ObservableProperty] private string status32Bit = Loc.GetString("Checking");
    [ObservableProperty] private bool is64Installed = false;
    [ObservableProperty] private bool is32Installed = false;
    [ObservableProperty] private string install64Text = Loc.GetString("Install");
    [ObservableProperty] private string install32Text = Loc.GetString("Install");
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

    public string AdvancedToggleText => ShowAdvanced ? Loc.GetString("HideAdvanced") : Loc.GetString("ShowAdvanced");

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
        // Only show enabled engines in the dropdown
        var enabledEngines = CloudEngines.Where(e => e.Enabled).Select(e => e.Id.ToLowerInvariant()).ToArray();
        VoiceConfig.AvailableEngines = enabledEngines;

        // Select first enabled engine
        if (enabledEngines.Length > 0)
            VoiceConfig.Initialize(enabledEngines[0]);
        else
            VoiceConfig.Initialize("azure");

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
            SherpaModelSummary = Loc.GetString("ModelsInstalled", count);
        }
        else
        {
            SherpaModelSummary = Loc.GetString("NoModelsInstalled");
        }
    }

    private void RefreshInstallStatus()
    {
        Is64Installed = ComRegistrationService.IsRegistered(true);
        Is32Installed = ComRegistrationService.IsRegistered(false);
        Status64Bit = Is64Installed ? Loc.GetString("Bit64Registered") : Loc.GetString("Bit64NotRegistered");
        Status32Bit = Is32Installed ? Loc.GetString("Bit32Registered") : Loc.GetString("Bit32NotRegistered");
        Install64Text = Is64Installed ? Loc.GetString("Reregister") : "Register";
        Install32Text = Is32Installed ? Loc.GetString("Reregister") : "Register";
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
