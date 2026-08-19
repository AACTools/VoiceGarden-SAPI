using System;
using System.Collections.Generic;
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
/// Tab 1 — selectable catalogue of every available engine with
/// online/offline, credentials and language filters.
/// </summary>
public partial class EnginesViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _suppressPersist;

    public EnginesViewModel(MainViewModel owner)
    {
        _owner = owner;
        BuildCatalog();
    }

    public ObservableCollection<EngineCatalogItem> AllEngines { get; } = new();
    public ObservableCollection<EngineCatalogItem> FilteredEngines { get; } = new();

    /// <summary>"All languages" placeholder + every language we have data for.</summary>
    public ObservableCollection<string> LanguageOptions { get; } = new();

    [ObservableProperty] private int typeFilterIndex;
    [ObservableProperty] private int credsFilterIndex;
    [ObservableProperty] private string? languageFilter;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private int hiddenUnknownLanguageCount;
    [ObservableProperty] private bool credentialsNeeded;

    public IReadOnlyList<string> TypeFilterOptions { get; } = new[]
    {
        Loc.GetString("FilterAllTypes"),
        Loc.GetString("FilterOfflineOnly"),
        Loc.GetString("FilterOnlineOnly"),
    };

    public IReadOnlyList<string> CredsFilterOptions { get; } = new[]
    {
        Loc.GetString("FilterAllEngines"),
        Loc.GetString("FilterNoCreds"),
        Loc.GetString("FilterCredsNeeded"),
    };

    public string SelectedSummary => Loc.GetString("EnginesSelectedSummary", SelectedCount, AllEngines.Count);

    partial void OnTypeFilterIndexChanged(int value) => ApplyFilter();
    partial void OnCredsFilterIndexChanged(int value) => ApplyFilter();
    partial void OnLanguageFilterChanged(string? value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(SelectedSummary));

    private void BuildCatalog()
    {
        // Selection change handlers fire while initial state is applied —
        // the owner has not wired us up yet, so only refresh counts.
        _suppressPersist = true;
        try
        {
            AllEngines.Clear();

            // Offline — SherpaOnnx models
            var sherpa = new EngineCatalogItem
            {
                Id = "sherpaonnx",
                DisplayName = Loc.GetString("SherpaOnnxEngineName"),
                Description = Loc.GetString("SherpaOnnxEngineDesc"),
                Kind = EngineKind.OfflineModel,
                IsSelected = _owner.SherpaEnabled,
            };
            AddItem(sherpa);

            // Online, free — Edge Read-Aloud voices
            var edge = new EngineCatalogItem
            {
                Id = "edge",
                DisplayName = Loc.GetString("EdgeEngineName"),
                Description = Loc.GetString("EdgeEngineDesc"),
                Kind = EngineKind.CloudFree,
                IsSelected = _owner.EdgeEnabled,
            };
            AddItem(edge);

            // Online, credentialed — discovered from the Rust wrapper
            foreach (var def in EngineDefinition.DiscoverAll())
            {
                var setting = _owner.CloudEngines.FirstOrDefault(s => s.Id.Equals(def.Id, StringComparison.OrdinalIgnoreCase));
                if (setting == null) continue;

                var item = new EngineCatalogItem
                {
                    Id = def.Id,
                    DisplayName = def.DisplayName,
                    Description = DescribeEngine(def),
                    Kind = EngineKind.CloudCreds,
                    CloudSetting = setting,
                    IsSelected = setting.Enabled,
                };
                AddItem(item);

                // Engines with a static voice list in the wrapper expose
                // their languages without credentials or a fetch.
                if (StaticEngineLanguages.TryGetValue(def.Id, out var langs))
                    UpdateEngineLanguages(def.Id, langs);
            }
        }
        finally
        {
            _suppressPersist = false;
        }

        RefreshLanguageOptions();
        ApplyFilter();
        RefreshSelectedCount();
    }

    /// <summary>
    /// Languages for cloud engines whose voice list is hardcoded in the
    /// wrapper (no voice-list API to query). Mined from rust-tts-wrapper
    /// src/cloud_engine.rs `static_voices()` at commit 703f27c — do not
    /// extend by guessing; engines with a voices_url populate from live
    /// fetches instead (see LoadEdgeLanguagesAsync / the Voices tab).
    /// </summary>
    private static readonly Dictionary<string, string[]> StaticEngineLanguages = new()
    {
        ["openai"] = new[] { "en-US" },
        ["hume"] = new[] { "en-US" },
        ["mistral"] = new[] { "en-US", "de-DE", "es-ES", "fr-FR", "pt-BR", "it-IT" },
        ["unrealspeech"] = new[] { "en-US" },
        ["xai"] = new[] { "en-US", "ur-PK" },
        ["modelslab"] = new[] { "en-US" },
    };

    private void AddItem(EngineCatalogItem item)
    {
        item.BuildSearchTokens();
        item.SelectionChanged += OnItemSelectionChanged;
        AllEngines.Add(item);
    }

    private static string DescribeEngine(EngineDefinition def)
    {
        var hasRegion = def.CredentialKeys.Contains("region");
        var hasSecret = def.CredentialKeys.Contains("secretAccessKey");
        var hasUser = def.CredentialKeys.Contains("userId");
        if (hasSecret) return Loc.GetString("EngineDescKeySecretRegion", def.DisplayName);
        if (hasRegion) return Loc.GetString("EngineDescKeyRegion", def.DisplayName);
        if (hasUser) return Loc.GetString("EngineDescKeyUserId", def.DisplayName);
        return Loc.GetString("EngineDescKeyOnly", def.DisplayName);
    }

    private void OnItemSelectionChanged(EngineCatalogItem item, bool value)
    {
        if (_suppressPersist)
        {
            RefreshSelectedCount();
            return;
        }

        switch (item.Kind)
        {
            case EngineKind.OfflineModel:
                _owner.SetSherpaEngineEnabled(value);
                break;
            case EngineKind.CloudFree:
                _owner.SetEdgeEngineEnabled(value);
                break;
            case EngineKind.CloudCreds:
                if (item.CloudSetting != null) item.CloudSetting.Enabled = value; // persists via MainViewModel handler
                break;
        }

        RefreshSelectedCount();
        _owner.OnEngineSelectionChanged();
    }

    private void RefreshSelectedCount()
    {
        SelectedCount = AllEngines.Count(e => e.IsSelected);
    }

    /// <summary>Selected engines that need credentials (drives the Credentials tab state).</summary>
    public List<EngineCatalogItem> SelectedCredsEngines() =>
        AllEngines.Where(e => e.IsSelected && e.NeedsCredentials).ToList();

    /// <summary>All selected engines as ids (drives the Voices tab aggregation).</summary>
    public List<EngineCatalogItem> SelectedEngines() =>
        AllEngines.Where(e => e.IsSelected).ToList();

    /// <summary>Refresh the "key stored" hint badges from current settings.</summary>
    public void RefreshKeyHints()
    {
        foreach (var item in AllEngines)
            item.HasStoredKey = item.CloudSetting?.HasKey ?? false;
    }

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var item in FilteredEngines) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearVisible()
    {
        foreach (var item in FilteredEngines) item.IsSelected = false;
    }

    [RelayCommand]
    private void GoToCredentials()
    {
        if (CredentialsNeeded) _owner.GoToCredentials();
    }

    [RelayCommand]
    private void GoToVoices() => _owner.GoToVoices();

    /// <summary>
    /// Merge known languages for an engine (Sherpa catalog languages or
    /// languages seen when its voice list was fetched) into the filter.
    /// Language names feed the dropdown; names + ISO codes feed search.
    /// </summary>
    public void UpdateEngineLanguages(string engineId, IEnumerable<string> languages, IEnumerable<string>? isoCodes = null)
    {
        var item = AllEngines.FirstOrDefault(e => e.Id.Equals(engineId, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;

        var added = false;
        foreach (var lang in languages)
        {
            if (string.IsNullOrWhiteSpace(lang)) continue;
            if (item.Languages.Add(lang)) added = true;
            EngineCatalogItem.AddLanguageTokens(item.SearchTokens, lang);
        }
        foreach (var code in isoCodes ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(code)) continue;
            if (item.SearchTokens.Add(code.Trim())) added = true;
        }
        if (added)
        {
            RefreshLanguageOptions();
            ApplyFilter();
            item.NotifyLanguagesChanged();
        }
    }

    /// <summary>Load the SherpaOnnx catalog in the background to learn its languages.</summary>
    public async Task LoadSherpaLanguagesAsync()
    {
        try
        {
            var langs = await Task.Run(async () =>
            {
                var catalog = await SherpaModelService.LoadCatalogAsync();
                var names = catalog
                    .SelectMany(c => c.Language ?? Enumerable.Empty<SherpaModelService.CatalogLanguage>())
                    .Select(l => l.LanguageName)
                    .Where(n => !string.IsNullOrWhiteSpace(n));
                // ISO 639-3 codes straight from the catalog ("ara", "eng", …)
                // so prefix searches on codes work without a fetch.
                var codes = catalog
                    .SelectMany(c => c.Language ?? Enumerable.Empty<SherpaModelService.CatalogLanguage>())
                    .Select(l => l.LangCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c));
                return (names: names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        codes: codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            });
            UpdateEngineLanguages("sherpaonnx", langs.names, langs.codes);
        }
        catch
        {
            // Language filter is best-effort; the Engines tab works without it.
        }
    }

    /// <summary>
    /// Fetch the free Edge Read-Aloud voice list (no credentials) so Edge
    /// languages are searchable/filterable from first launch.
    /// </summary>
    public async Task LoadEdgeLanguagesAsync()
    {
        try
        {
            var langs = await Task.Run(() =>
            {
                using var client = new RustTtsWrapper.TtsClient("edge", new Dictionary<string, string>());
                return client.GetVoices()
                    .Select(v => string.IsNullOrEmpty(v.Language) ? "en-US" : v.Language)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });
            UpdateEngineLanguages("edge", langs);
        }
        catch
        {
            // Best-effort: Edge still appears via name/description search.
        }
    }

    private void RefreshLanguageOptions()
    {
        var all = Loc.GetString("FilterAllLanguages");
        var selected = LanguageFilter;
        LanguageOptions.Clear();
        LanguageOptions.Add(all);
        foreach (var lang in AllEngines.SelectMany(e => e.Languages).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l, StringComparer.CurrentCulture))
            LanguageOptions.Add(lang);

        if (selected != null && LanguageOptions.Contains(selected))
            LanguageFilter = selected;
        else
            LanguageFilter = all;
    }

    private void ApplyFilter()
    {
        var allLabel = Loc.GetString("FilterAllLanguages");
        var search = SearchFilterText();
        var languageActive = !string.IsNullOrEmpty(LanguageFilter) && LanguageFilter != allLabel;

        FilteredEngines.Clear();
        int hiddenUnknown = 0;

        foreach (var e in AllEngines)
        {
            if (TypeFilterIndex == 1 && !e.IsOffline) continue;
            if (TypeFilterIndex == 2 && e.IsOffline) continue;
            if (CredsFilterIndex == 1 && e.NeedsCredentials) continue;
            if (CredsFilterIndex == 2 && !e.NeedsCredentials) continue;

            if (languageActive)
            {
                if (!e.HasLanguages) { hiddenUnknown++; continue; }
                if (!e.Languages.Contains(LanguageFilter!)) continue;
            }

            if (search.Length > 0 && !MatchesSearch(e, search)) continue;

            FilteredEngines.Add(e);
        }

        HiddenUnknownLanguageCount = hiddenUnknown;
        OnPropertyChanged(nameof(LanguageFilterNote));
    }

    /// <summary>
    /// Search matches engine name, id and description (contains, as before)
    /// plus supported languages — language name in English or native,
    /// BCP-47 tag and ISO 639-1/639-3 code — as a case-insensitive prefix
    /// match, so "ara" and "arabic" both surface Arabic-capable engines.
    /// </summary>
    private static bool MatchesSearch(EngineCatalogItem e, string search)
    {
        if (e.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        if (e.Id.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        if (e.Description.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var token in e.SearchTokens)
        {
            if (token.StartsWith(search, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public string? LanguageFilterNote
    {
        get
        {
            var allLabel = Loc.GetString("FilterAllLanguages");
            var languageActive = !string.IsNullOrEmpty(LanguageFilter) && LanguageFilter != allLabel;
            return languageActive && HiddenUnknownLanguageCount > 0
                ? Loc.GetString("LanguageFilterHiddenNote", HiddenUnknownLanguageCount)
                : null;
        }
    }

    private string SearchFilterText() => SearchText?.Trim() ?? "";
}
