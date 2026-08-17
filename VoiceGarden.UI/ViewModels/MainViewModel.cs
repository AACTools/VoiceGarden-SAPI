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

/// <summary>
/// Tab shell for the 3-tab accessible flow (Engines → Credentials → Voices)
/// plus an Advanced tab that keeps every pre-existing function available.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    public const int EnginesTabIndex = 0;
    public const int CredentialsTabIndex = 1;
    public const int VoicesTabIndex = 2;
    public const int AdvancedTabIndex = 3;

    public MainViewModel()
    {
        AppName = BrandingConfig.AppName;

        // Track app launch (only if opted in)
        AnalyticsService.Track("app_launched");

        // Load settings from registry
        sherpaEnabled = !RegistryService.GetFlag("NoSherpaVoices", !BrandingConfig.DefaultSherpaEnabled);
        edgeEnabled = !RegistryService.GetFlag("NoEdgeVoices");
        AnalyticsEnabled = Services.AnalyticsService.IsEnabled;
        LogLevelIndex = RegistryService.GetDword("LogLevel", 0);

        // Load cloud engines
        foreach (var def in EngineDefinition.DiscoverAll())
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

            // Track subscription for cleanup
            _eventSubscriptions.Add(Disposable.Create(() =>
                setting.PropertyChanged -= handler));

            CloudEngines.Add(setting);
        }

        // Build the tab view models once the engine settings exist
        Engines = new EnginesViewModel(this);
        Credentials = new CredentialsViewModel(this);
        Voices = new VoicesViewModel(this);
        OnEngineSelectionChanged();
        Engines.RefreshKeyHints();

        // Learn the Sherpa catalog languages and the free Edge voice-list
        // languages for the Engines filter/search in the background
        _ = Engines.LoadSherpaLanguagesAsync();
        _ = Engines.LoadEdgeLanguagesAsync();

        // Count installed SherpaOnnx models
        UpdateSherpaModelCount();

        // Check adapter installation status
        RefreshInstallStatus();
    }

    // Track event subscriptions for cleanup
    private readonly List<IDisposable> _eventSubscriptions = new List<IDisposable>();

    public void Dispose()
    {
        _eventSubscriptions.Clear();
    }

    [ObservableProperty] private string appName = "VoiceGarden";

    // ----- Engine enable state (persisted; driven from the Engines tab) -----

    [ObservableProperty] private bool sherpaEnabled;
    [ObservableProperty] private bool edgeEnabled;

    partial void OnSherpaEnabledChanged(bool value) => RegistryService.SetFlag("NoSherpaVoices", !value);

    partial void OnEdgeEnabledChanged(bool value)
    {
        RegistryService.SetFlag("NoEdgeVoices", !value);
        AnalyticsService.Track("engine_toggled", ("engine", "edge"), ("enabled", value));
    }

    internal void SetSherpaEngineEnabled(bool value) => SherpaEnabled = value;
    internal void SetEdgeEngineEnabled(bool value) => EdgeEnabled = value;

    // ----- Tabs -----

    [ObservableProperty] private int selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        var name = value switch
        {
            CredentialsTabIndex => Loc.GetString("TabCredentials"),
            VoicesTabIndex => Loc.GetString("TabVoices"),
            AdvancedTabIndex => Loc.GetString("Advanced"),
            _ => Loc.GetString("TabEngines"),
        };
        AnnounceViewChange(name);

        // Lazy-load the full model manager the first time Advanced opens
        if (value == AdvancedTabIndex && SherpaModels.TotalCount == 0)
            _ = SherpaModels.LoadCatalogCommand.ExecuteAsync(null);

        // Voices load themselves the first time the tab is shown (and again
        // if the engine selection changed since the last load) — the user
        // should never have to press Load.
        if (value == VoicesTabIndex && (Voices.TotalCount == 0 || Voices.IsStale))
            _ = Voices.LoadVoicesCommand.ExecuteAsync(null);
    }

    public EnginesViewModel Engines { get; private set; } = null!;
    public CredentialsViewModel Credentials { get; private set; } = null!;
    public VoicesViewModel Voices { get; private set; } = null!;
    public SherpaModelsViewModel SherpaModels { get; } = new();

    /// <summary>Selected engines that need credentials — enables the Credentials tab.</summary>
    [ObservableProperty] private int selectedCredsEngineCount;

    public bool CredentialsTabEnabled => SelectedCredsEngineCount > 0;

    public string CredentialsTabHeader => Loc.GetString("TabCredentials");

    public string CredentialsTabState => CredentialsTabEnabled
        ? Loc.GetString("TabCredentialsCount", SelectedCredsEngineCount)
        : Loc.GetString("TabCredentialsNotNeeded");

    public string VoicesTabHeader => Loc.GetString("TabVoices");

    public string VoicesTabState { get; private set; } = "";

    public string EnginesTabHeader => Loc.GetString("TabEngines");

    public string EnginesTabState { get; private set; } = "";

    /// <summary>Called by the Engines tab after any selection change.</summary>
    internal void OnEngineSelectionChanged()
    {
        SelectedCredsEngineCount = Engines.SelectedCredsEngines().Count;
        OnPropertyChanged(nameof(CredentialsTabEnabled));
        OnPropertyChanged(nameof(CredentialsTabState));
        EnginesTabState = Loc.GetString("TabEnginesCount", Engines.SelectedCount);
        OnPropertyChanged(nameof(EnginesTabState));
        Engines.CredentialsNeeded = CredentialsTabEnabled;

        Credentials.Refresh();
        Engines.RefreshKeyHints();
        Voices.MarkStale();

        // If the Credentials tab just became unnecessary while it is open, fall back to Engines.
        if (!CredentialsTabEnabled && SelectedTabIndex == CredentialsTabIndex)
            SelectedTabIndex = EnginesTabIndex;
    }

    /// <summary>Called by the Voices tab after a load so the header count refreshes.</summary>
    internal void OnVoicesLoaded()
    {
        VoicesTabState = Loc.GetString("TabVoicesCount", Voices.TotalCount);
        OnPropertyChanged(nameof(VoicesTabState));
    }

    partial void OnSelectedCredsEngineCountChanged(int value)
    {
        OnPropertyChanged(nameof(CredentialsTabEnabled));
        OnPropertyChanged(nameof(CredentialsTabState));
    }

    internal void GoToEngines() => SelectedTabIndex = EnginesTabIndex;

    internal void GoToCredentials()
    {
        if (CredentialsTabEnabled) SelectedTabIndex = CredentialsTabIndex;
    }

    internal void GoToVoices() => SelectedTabIndex = VoicesTabIndex;

    // ----- Advanced tab state -----

    [ObservableProperty] private bool analyticsEnabled = false;

    partial void OnAnalyticsEnabledChanged(bool value)
    {
        Services.AnalyticsService.IsEnabled = value;
        if (value) Services.AnalyticsService.Track("analytics_opted_in");
    }

    [ObservableProperty] private int logLevelIndex = 0;
    partial void OnLogLevelIndexChanged(int value) => RegistryService.SetDword("LogLevel", value);

    [ObservableProperty] private string status64Bit = Loc.GetString("Checking");
    [ObservableProperty] private string status32Bit = Loc.GetString("Checking");
    [ObservableProperty] private bool is64Installed = false;
    [ObservableProperty] private bool is32Installed = false;
    [ObservableProperty] private string install64Text = Loc.GetString("Install");
    [ObservableProperty] private string install32Text = Loc.GetString("Install");
    [ObservableProperty] private string sherpaModelSummary = "";

    public string AboutText =>
        $"VoiceGarden v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}\n\n" +
        "SAPI Voice Adapter Configuration Tool\n\n" +
        $"RustTtsWrapper: {typeof(RustTtsWrapper.TtsClient).Assembly.GetName().Version}\n" +
        "Engines: Azure, Edge, OpenAI, ElevenLabs, Google, Polly, Cartesia, Deepgram,\n" +
        "SherpaOnnx (offline), Watson, PlayHT, Wit.ai, Gemini, and more\n\n" +
        "https://github.com/AACTools/VoiceGarden-SAPI";

    public ObservableCollection<CloudEngineSetting> CloudEngines { get; } = new();

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
    private void OpenLogs()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceGardenSAPIAdapter");
        if (Directory.Exists(logDir))
            Process.Start("explorer.exe", $"\"{logDir}\"");
    }

    [ObservableProperty] private string screenReaderAnnouncement = "";

    private void AnnounceViewChange(string viewName)
    {
        ScreenReaderAnnouncement = $"{viewName}";
    }

    private static string Cap(string s) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s);
}
