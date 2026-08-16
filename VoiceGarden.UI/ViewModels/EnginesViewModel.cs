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

    private void AddItem(EngineCatalogItem item)
    {
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
    /// </summary>
    public void UpdateEngineLanguages(string engineId, IEnumerable<string> languages)
    {
        var item = AllEngines.FirstOrDefault(e => e.Id.Equals(engineId, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;

        var added = false;
        foreach (var lang in languages)
        {
            if (string.IsNullOrWhiteSpace(lang)) continue;
            if (item.Languages.Add(lang)) added = true;
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
                return catalog
                    .SelectMany(c => c.Language ?? Enumerable.Empty<SherpaModelService.CatalogLanguage>())
                    .Select(l => l.LanguageName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });
            UpdateEngineLanguages("sherpaonnx", langs);
        }
        catch
        {
            // Language filter is best-effort; the Engines tab works without it.
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

            if (search.Length > 0 &&
                !e.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !e.Id.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !e.Description.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;

            FilteredEngines.Add(e);
        }

        HiddenUnknownLanguageCount = hiddenUnknown;
        OnPropertyChanged(nameof(LanguageFilterNote));
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
