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

public enum AggregatedVoiceKind
{
    /// <summary>SherpaOnnx offline model from the downloadable catalog.</summary>
    SherpaModel,
    /// <summary>Cloud voice fetched with credentials.</summary>
    Cloud,
    /// <summary>Edge Read-Aloud voice (no credentials).</summary>
    EdgeFree,
}

/// <summary>One voice row in the aggregated Voices tab.</summary>
public partial class VoiceEntry : ObservableObject
{
    public string EngineId { get; init; } = "";
    public string EngineName { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Language { get; init; } = "";
    public string Gender { get; init; } = "";
    public string Quality { get; init; } = "";
    public AggregatedVoiceKind Kind { get; init; }
    public int SampleRate { get; init; } = 24000;

    /// <summary>Catalog entry for downloadable Sherpa models; null for cloud voices.</summary>
    public SherpaModelService.CatalogModel? CatalogModel { get; init; }

    public bool IsSherpa => Kind == AggregatedVoiceKind.SherpaModel;
    public bool IsCloud => Kind != AggregatedVoiceKind.SherpaModel;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isInstalled;
    [ObservableProperty] private bool isDownloaded;
    [ObservableProperty] private bool isDownloading;
    [ObservableProperty] private int downloadProgress;
    [ObservableProperty] private string downloadStatus = "";

    /// <summary>Show the internal ID on the row — only set for duplicate display names.</summary>
    [ObservableProperty] private bool showId;

    public string TokenName => IsSherpa
        ? $"Sherpa-{Id}"
        : $"Cloud-{EngineId}-{Id}".Replace("/", "_").Replace("\\", "_");

    public bool CanPreview => !IsSherpa || IsDownloaded;

    public bool CanPromote => !IsInstalled && (!IsSherpa || IsDownloaded);

    public bool ShowDownload => IsSherpa && !IsDownloaded;

    public string QualityBadge => string.IsNullOrEmpty(Quality) || Quality == "unknown" ? "" : Quality;

    public string Quantization { get; init; } = "";

    /// <summary>Weight-type badge for quantized builds (int8/fp16/…); fp32 is the silent default.</summary>
    public string QuantizationBadge =>
        !string.IsNullOrEmpty(Quantization) && Quantization != "fp32" ? Quantization : "";

    /// <summary>Disambiguates rows that share a display name (e.g. the three Urdu script variants).</summary>
    public string AutomationLabel => Id == Name ? Name : $"{Name} ({Id})";

    public string GenderLabel => Gender is "Male" or "Female" ? Gender : "";

    public string SizeText => CatalogModel is { FileSizeMb: > 0 } m ? $"{m.FileSizeMb:F0} MB" : "";

    public string DetailsToolTip
    {
        get
        {
            var parts = new List<string> { $"{EngineName}: {Name}" };
            if (!string.IsNullOrEmpty(Id)) parts.Add(Id);
            if (!string.IsNullOrEmpty(Language)) parts.Add(Language);
            if (!string.IsNullOrEmpty(QualityBadge)) parts.Add($"Quality: {Quality}");
            if (!string.IsNullOrEmpty(GenderLabel)) parts.Add($"Voice: {Gender}");
            if (CatalogModel is { } cm)
            {
                if (!string.IsNullOrEmpty(cm.Quantization) && cm.Quantization != "fp32")
                    parts.Add($"Weights: {cm.Quantization}");
                if (!string.IsNullOrEmpty(cm.License)) parts.Add($"Licence: {cm.License}");
                if (!string.IsNullOrEmpty(cm.MinSherpaOnnxVersion)) parts.Add($"Needs sherpa-onnx {MinVersionText(cm)}+");
                if (cm.Deprecated == true) parts.Add("Deprecated upstream — inference keeps working");
            }
            return string.Join(" | ", parts);
        }
    }

    private static string MinVersionText(SherpaModelService.CatalogModel cm) =>
        cm.MinSherpaOnnxVersion ?? "";

    partial void OnIsDownloadedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanPromote));
        OnPropertyChanged(nameof(ShowDownload));
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(CanPromote));
}

/// <summary>
/// Tab 3 — every voice available from the engines selected in Tab 1,
/// aggregated: SherpaOnnx catalog models (installed + downloadable), Edge
/// Read-Aloud voices and credentialed cloud voices. Filters, preview and
/// promote (single + bulk) all live here.
/// </summary>
public partial class VoicesViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly HashSet<string> _loadedEngines = new(StringComparer.OrdinalIgnoreCase);

    public VoicesViewModel(MainViewModel owner)
    {
        _owner = owner;
        ResetFilterOptions();
    }

    public ObservableCollection<VoiceEntry> AllVoices { get; } = new();
    public ObservableCollection<VoiceEntry> FilteredVoices { get; } = new();

    public ObservableCollection<string> EngineOptions { get; } = new();
    public ObservableCollection<string> LanguageOptions { get; } = new();
    public ObservableCollection<string> GenderOptions { get; } = new();
    public ObservableCollection<string> QualityOptions { get; } = new();

    [ObservableProperty] private string? engineFilter;
    [ObservableProperty] private string? languageFilter;
    [ObservableProperty] private string? genderFilter;
    [ObservableProperty] private string? qualityFilter;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int selectedCount;
    [ObservableProperty] private int installedCount;

    /// <summary>Filter toggle: show only voices already installed to SAPI.</summary>
    [ObservableProperty] private bool installedOnly;

    partial void OnInstalledOnlyChanged(bool value) => ApplyFilter();

    /// <summary>Error banner at the top of the Voices tab (icon + text, cleared by the next action).</summary>
    [ObservableProperty] private string? errorText;

    /// <summary>Optional technical detail line under the banner (file paths, raw messages).</summary>
    [ObservableProperty] private string? errorDetail;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>True when the list is empty and idle — show the guiding empty state.</summary>
    public bool ShowEmptyState => !IsLoading && TotalCount == 0 && !HasError;

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    private void ShowError(string friendly, string? detail = null)
    {
        ErrorText = friendly;
        ErrorDetail = detail;
    }

    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    /// <summary>True when the loaded list no longer reflects the engine selection.</summary>
    public bool IsStale { get; private set; } = true;

    /// <summary>Flag for reload on the next Voices tab activation.</summary>
    public void MarkStale() => IsStale = true;

    private string AllEnginesLabel => Loc.GetString("FilterAnyEngine");
    private string AnyLanguageLabel => Loc.GetString("FilterAnyLanguage");
    private string AnyGenderLabel => Loc.GetString("FilterAnyGender");
    private string AnyQualityLabel => Loc.GetString("FilterAnyQuality");

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnEngineFilterChanged(string? value) => ApplyFilter();
    partial void OnLanguageFilterChanged(string? value) => ApplyFilter();
    partial void OnGenderFilterChanged(string? value) => ApplyFilter();
    partial void OnQualityFilterChanged(string? value) => ApplyFilter();

    public string CountSummary => Loc.GetString("VoicesCountSummary", TotalCount, SelectedCount, InstalledCount);

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(CountSummary));
    partial void OnInstalledCountChanged(int value) => OnPropertyChanged(nameof(CountSummary));

    [RelayCommand]
    private async Task LoadVoices()
    {
        if (IsLoading) return;
        ClearError();
        var selected = _owner.Engines.SelectedEngines();
        if (selected.Count == 0)
        {
            StatusText = Loc.GetString("VoicesNoEnginesSelected");
            ShowError(Loc.GetString("VoicesEmptyNoEngines"));
            return;
        }

        IsLoading = true;
        AllVoices.Clear();
        FilteredVoices.Clear();
        _loadedEngines.Clear();
        var notes = new List<string>();
        var skippedNoKey = new List<string>();

        try
        {
            foreach (var engine in selected)
            {
                switch (engine.Kind)
                {
                    case EngineKind.OfflineModel:
                        await LoadSherpaEntries(notes);
                        break;
                    case EngineKind.CloudFree:
                        await FetchCloudVoices("edge", engine.DisplayName, "", "", notes);
                        break;
                    case EngineKind.CloudCreds:
                    {
                        var setting = engine.CloudSetting;
                        if (setting == null) break;
                        if (!setting.HasKey)
                        {
                            skippedNoKey.Add(setting.DisplayName);
                            break;
                        }
                        await FetchCloudVoices(setting.Id, setting.DisplayName, setting.ApiKey ?? "", setting.Region ?? "", notes);
                        break;
                    }
                }
            }

            await RefreshInstalledStatus();
            RebuildFilterOptions();
            MarkDuplicateNames();
            ApplyFilter();
            UpdateCounts();
            _owner.OnVoicesLoaded();
            IsStale = false;

            // One quiet status line: summary + a single no-key note (the
            // per-engine hint when it's the only one, a count otherwise).
            var summary = Loc.GetString("VoicesLoaded", AllVoices.Count);
            var suffixes = new List<string>();
            if (skippedNoKey.Count == 1)
                suffixes.Add(Loc.GetString("VoicesMissingKey", skippedNoKey[0]));
            else if (skippedNoKey.Count > 1)
                suffixes.Add(Loc.GetString("VoicesSkippedNoKey", skippedNoKey.Count));
            if (notes.Count > 0) suffixes.AddRange(notes);
            StatusText = suffixes.Count > 0 ? $"{summary} — {string.Join("; ", suffixes)}" : summary;

            // Fetch failures and missing keys go to the banner so they are
            // seen even when the status line scrolls away.
            var bannerProblems = new List<string>();
            if (skippedNoKey.Count > 0)
                bannerProblems.Add(skippedNoKey.Count == 1
                    ? Loc.GetString("VoicesMissingKey", skippedNoKey[0])
                    : Loc.GetString("VoicesSkippedNoKey", skippedNoKey.Count));
            bannerProblems.AddRange(notes.Where(n => n.Contains(':')));
            if (bannerProblems.Count > 0)
                ShowError(string.Join("  ·  ", bannerProblems));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Show the internal model ID only where the display name is duplicated
    /// (e.g. the three Urdu script variants) — otherwise the row stays clean.
    /// </summary>
    private void MarkDuplicateNames()
    {
        var duplicated = AllVoices
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var v in AllVoices)
            v.ShowId = duplicated.Contains(v.Name);
    }

    private async Task LoadSherpaEntries(List<string> notes)
    {
        StatusText = Loc.GetString("LoadingCatalog");
        try
        {
            // Catalog load + item mapping off the UI thread (1300+ models).
            var entries = await Task.Run(async () =>
            {
                var catalog = (await SherpaModelService.LoadCatalogAsync())
                    // fp16 builds SIGABRT the wrapper's CPU-only ONNX runtime —
                    // hide them from the downloadable list.
                    .Where(c => !c.Url.Contains("fp16", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var installed = SherpaModelService.ScanInstalledModels();

                var list = new List<VoiceEntry>(catalog.Count);
                foreach (var cat in catalog)
                {
                    var inst = installed.FirstOrDefault(i => i.Id == cat.Id);
                    list.Add(new VoiceEntry
                    {
                        Kind = AggregatedVoiceKind.SherpaModel,
                        EngineId = "sherpaonnx",
                        EngineName = Loc.GetString("SherpaOnnxEngineName"),
                        Id = cat.Id,
                        Name = string.IsNullOrEmpty(cat.Name) ? cat.Id : cat.Name,
                        Language = cat.Language?.FirstOrDefault()?.LanguageName ?? Loc.GetString("UnknownLanguage"),
                        Quality = cat.Quality ?? "",
                        Quantization = cat.Quantization ?? "",
                        Gender = SherpaModelService.DeriveSherpaGender(cat.Id, cat.Name, cat.NumSpeakers ?? 1),
                        SampleRate = cat.SampleRate ?? 24000,
                        CatalogModel = cat,
                        IsDownloaded = inst != null,
                    });
                }

                // Installed models not present in the catalog
                foreach (var inst2 in installed)
                {
                    if (list.Any(v => v.Id == inst2.Id)) continue;
                    list.Add(new VoiceEntry
                    {
                        Kind = AggregatedVoiceKind.SherpaModel,
                        EngineId = "sherpaonnx",
                        EngineName = Loc.GetString("SherpaOnnxEngineName"),
                        Id = inst2.Id,
                        Name = inst2.Id,
                        Language = Loc.GetString("UnknownLanguage"),
                        IsDownloaded = true,
                    });
                }

                return list;
            });

            foreach (var e in entries) AllVoices.Add(e);
            _loadedEngines.Add("sherpaonnx");
        }
        catch (Exception ex)
        {
            notes.Add(Loc.GetString("VoicesFetchFailed", Loc.GetString("SherpaOnnxEngineName"), ex.Message));
        }
    }

    private async Task FetchCloudVoices(string engineId, string engineName, string key, string region, List<string> notes)
    {
        StatusText = Loc.GetString("FetchingVoices", engineName);
        try
        {
            var voices = await Task.Run(() =>
            {
                var creds = TtsCredentialBuilder.Build(engineId, key, region) ?? new Dictionary<string, string>();
                using var client = new RustTtsWrapper.TtsClient(engineId, creds);
                return client.GetVoices();
            });

            var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in voices)
            {
                var lang = string.IsNullOrEmpty(v.Language) ? "en-US" : v.Language;
                languages.Add(lang);
                AllVoices.Add(new VoiceEntry
                {
                    Kind = engineId == "edge" ? AggregatedVoiceKind.EdgeFree : AggregatedVoiceKind.Cloud,
                    EngineId = engineId,
                    EngineName = engineName,
                    Id = v.Id ?? "",
                    Name = v.Name ?? v.Id ?? "",
                    Language = lang,
                    // Only surface known genders; "Unknown" would be list noise
                    Gender = v.Gender is "Male" or "Female" ? v.Gender : "",
                });
            }
            _loadedEngines.Add(engineId);

            // Feed the languages back to the Engines tab filter.
            _owner.Engines.UpdateEngineLanguages(engineId, languages);
        }
        catch (Exception ex)
        {
            notes.Add(Loc.GetString("VoicesFetchFailed", engineName, ex.Message));
        }
    }

    [RelayCommand]
    private async Task RefreshInstalled()
    {
        await RefreshInstalledStatus();
        UpdateCounts();
        StatusText = Loc.GetString("RescannedVoices", InstalledCount);
    }

    private async Task RefreshInstalledStatus()
    {
        var promoted = await Task.Run(VoicePromotionService.ListPromoted);
        var promotedTokens = promoted
            .Select(p => p.TokenName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var v in AllVoices)
            v.IsInstalled = promotedTokens.Contains(v.TokenName);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var v in FilteredVoices) v.IsSelected = true;
        UpdateCounts();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var v in AllVoices) v.IsSelected = false;
        UpdateCounts();
    }

    [RelayCommand]
    private async Task Preview(VoiceEntry? voice)
    {
        if (voice == null) return;

        if (voice.IsSherpa && !voice.IsDownloaded)
        {
            StatusText = Loc.GetString("DownloadModelFirst");
            return;
        }

        StatusText = Loc.GetString("PreviewingVoice", voice.Name);
        try
        {
            if (voice.IsSherpa)
            {
                // Scan + path derivation off the UI thread (the scan takes
                // the archive lock and loads the catalog — seconds, and can
                // deadlock against a finishing download if run on the UI thread).
                var resolved = await Task.Run(() =>
                {
                    var installed = SherpaModelService.GetInstalledModel(voice.Id);
                    if (installed?.ModelPath == null) return (modelId: "", basePath: "");

                    var p = System.IO.Path.GetDirectoryName(installed.ModelPath);
                    while (p != null && System.IO.Path.GetFileName(p) != "models")
                        p = System.IO.Path.GetDirectoryName(p);
                    if (p == null || System.IO.Path.GetFileName(p) != "models")
                        return (modelId: "", basePath: "");

                    var rel = System.IO.Path.GetRelativePath(p, System.IO.Path.GetDirectoryName(installed.ModelPath)!);
                    return (modelId: rel.Split(System.IO.Path.DirectorySeparatorChar)[0], basePath: p);
                });

                if (string.IsNullOrEmpty(resolved.modelId))
                {
                    StatusText = Loc.GetString("ModelFilesNotFound");
                    return;
                }
                var modelId = resolved.modelId;
                var modelBasePath = resolved.basePath;

                var audio = await Task.Run(() =>
                {
                    using var client = new RustTtsWrapper.TtsClient("sherpaonnx", new Dictionary<string, string>
                    {
                        { "modelId", modelId },
                        { "modelPath", modelBasePath }
                    });
                    return client.SynthToBytes(AudioPreview.GetSherpaPreviewText(voice.Id, voice.Name));
                });

                if (audio.Length > 0)
                {
                    AudioPreview.PlayPcm(audio, voice.SampleRate > 0 ? voice.SampleRate : 24000, "vg_sherpa_");
                    StatusText = Loc.GetString("PreviewingVoice", voice.Name);
                }
                else
                {
                    StatusText = Loc.GetString("NoAudio");
                }
            }
            else
            {
                var (key, region) = GetEngineCredentials(voice.EngineId);
                if (voice.Kind == AggregatedVoiceKind.Cloud && string.IsNullOrWhiteSpace(key))
                {
                    StatusText = Loc.GetString("EnterApiKeyFirst");
                    return;
                }

                var engineId = voice.EngineId;
                var voiceId = voice.Id;
                var audio = await Task.Run(() =>
                {
                    var creds = TtsCredentialBuilder.Build(engineId, key, region) ?? new Dictionary<string, string>();
                    using var client = new RustTtsWrapper.TtsClient(engineId, creds);
                    client.SetVoice(voiceId);
                    return client.SynthToBytes($"Hello, my name is {voice.Name}.");
                });

                if (audio.Length > 0)
                {
                    AudioPreview.PlayPcm(audio, 24000, "voicegarden_preview_");
                    StatusText = Loc.GetString("PreviewingVoice", voice.Name);
                }
                else
                {
                    StatusText = Loc.GetString("NoAudio");
                }
            }
        }
        catch (RustTtsWrapper.TtsException ex)
        {
            StatusText = Loc.GetString("PreviewFailed", ex.Message);
            ShowError(Loc.GetString("PreviewFailedTitle", voice.Name), ex.Message);
        }
        catch (Exception ex)
        {
            StatusText = Loc.GetString("PreviewFailed", ex.Message);
            ShowError(Loc.GetString("PreviewFailedTitle", voice.Name), ex.Message);
        }
    }

    [RelayCommand]
    private async Task Download(VoiceEntry? voice)
    {
        if (voice == null || !voice.IsSherpa || voice.CatalogModel == null || voice.IsDownloading) return;
        ClearError();

        var sizeMb = voice.CatalogModel.FileSizeMb > 0 ? $"{voice.CatalogModel.FileSizeMb:F0}MB" : "??MB";
        voice.IsDownloading = true;
        voice.DownloadProgress = 0;
        voice.DownloadStatus = Loc.GetString("DownloadingModel", voice.Id, sizeMb);
        StatusText = Loc.GetString("DownloadingModel", voice.Name, sizeMb);

        try
        {
            var progress = new Progress<(int pct, string msg)>(p =>
            {
                voice.DownloadProgress = p.pct;
                voice.DownloadStatus = $"{p.pct}% - {voice.Id}";
            });

            await SherpaModelService.DownloadModelAsync(voice.CatalogModel, (IProgress<(int, string)>)progress);
            voice.IsDownloaded = true;
            voice.DownloadProgress = 100;
            voice.DownloadStatus = Loc.GetString("DownloadedStatus");
            StatusText = Loc.GetString("DownloadedStatus");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            voice.DownloadStatus = $"Failed: {detail}";
            StatusText = Loc.GetString("DownloadFailedSingle", voice.Name);
            ShowError(Loc.GetString("DownloadFailedTitle", voice.Name), detail);
        }
        finally
        {
            voice.IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task Promote(VoiceEntry? voice)
    {
        if (voice == null) return;

        if (voice.IsSherpa && !voice.IsDownloaded)
        {
            StatusText = Loc.GetString("DownloadModelFirst");
            return;
        }

        IsLoading = true;
        try
        {
            var (ok, message) = voice.IsSherpa
                ? await PromoteSherpaModels(new[] { voice.Id })
                : await PromoteCloudVoice(voice);

            if (ok)
            {
                voice.IsInstalled = true;
                StatusText = Loc.GetString("VoicesPromotedSingle", voice.Name);
                ClearError();
            }
            else
            {
                StatusText = message;
                ShowError(message);
            }
        }
        finally
        {
            IsLoading = false;
            UpdateCounts();
        }
    }

    [RelayCommand]
    private async Task PromoteSelected()
    {
        var selected = AllVoices.Where(v => v.IsSelected && !v.IsInstalled).ToList();
        if (selected.Count == 0)
        {
            StatusText = Loc.GetString("SelectVoicesFirst");
            return;
        }

        IsLoading = true;
        int promoted = 0, failed = 0;
        var lastError = "";

        try
        {
            // Sherpa models — one elevated .reg import for the whole batch
            var sherpaIds = selected.Where(v => v.IsSherpa && v.IsDownloaded).Select(v => v.Id).ToList();
            if (sherpaIds.Count > 0)
            {
                var (ok, message) = await PromoteSherpaModels(sherpaIds);
                if (ok) promoted += sherpaIds.Count;
                else { failed += sherpaIds.Count; lastError = message; }
            }

            // Cloud / Edge voices — one by one
            foreach (var voice in selected.Where(v => v.IsCloud))
            {
                var (ok, message) = await PromoteCloudVoice(voice);
                if (ok) promoted++;
                else { failed++; lastError = message; }
            }

            await RefreshInstalledStatus();
            UpdateCounts();

            if (failed == 0)
            {
                StatusText = Loc.GetString("InstalledVoicesHKLM", promoted);
                ClearError();
            }
            else
            {
                var friendly = Loc.GetString("InstalledModelsFailed", promoted, failed);
                StatusText = friendly + (lastError.Length > 0 ? $" ({lastError})" : "");
                ShowError(friendly, lastError.Length > 0 ? lastError : null);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UnpromoteSelected()
    {
        var selected = AllVoices.Where(v => v.IsSelected && v.IsInstalled).ToList();
        if (selected.Count == 0)
        {
            StatusText = Loc.GetString("SelectVoicesFirst");
            return;
        }

        IsLoading = true;
        try
        {
            int removed = 0, failed = 0;
            foreach (var voice in selected)
            {
                var rc = await Task.Run(() => VoicePromotionService.UnpromoteElevated(voice.TokenName));
                if (rc == 0)
                {
                    voice.IsInstalled = false;
                    removed++;
                }
                else
                {
                    failed++;
                }
            }

            UpdateCounts();
            if (failed == 0)
            {
                StatusText = Loc.GetString("RemovedVoices", removed);
                ClearError();
            }
            else
            {
                var friendly = Loc.GetString("VoicesUnpromoteFailed", removed, failed);
                StatusText = friendly;
                ShowError(friendly);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<(bool ok, string message)> PromoteSherpaModels(IReadOnlyList<string> modelIds)
    {
        // Everything here (directory scan, catalog load, HKLM writes) runs off
        // the UI thread: ScanInstalledModels takes the archive lock, and if a
        // download's continuation is waiting on the UI thread to release it,
        // running this synchronously deadlocks the whole window.
        return await Task.Run(() =>
        {
            // Direct write works when running elevated
            var anyDirect = false;
            var failures = 0;
            foreach (var id in modelIds)
            {
                var installed = SherpaModelService.GetInstalledModel(id);
                if (installed?.ModelPath == null) { failures++; continue; }
                try
                {
                    SherpaModelService.PromoteSherpaModel(installed);
                    anyDirect = true;
                }
                catch
                {
                    failures++;
                }
            }

            if (anyDirect && failures == 0) return (true, "");

            // Not elevated — one .reg import for the whole batch via UAC
            var (promoted, failed, error) = SherpaModelService.PromoteModelsElevated(modelIds);
            if (promoted > 0) return (true, "");
            if (error == "UAC cancelled") return (false, Loc.GetString("InstallCancelled"));
            return (false, Loc.GetString("InstallFailed", error));
        });
    }

    private async Task<(bool ok, string message)> PromoteCloudVoice(VoiceEntry voice)
    {
        var (key, region) = GetEngineCredentials(voice.EngineId);
        var engineId = voice.EngineId;
        var voiceId = voice.Id;
        var gender = voice.Gender;
        var language = voice.Language;

        var rc = await Task.Run(() =>
            VoicePromotionService.PromoteElevated(engineId, voiceId, key, region, gender, language));

        return rc switch
        {
            0 => (true, ""),
            -2 => (false, Loc.GetString("InstallCancelled")),
            _ => (false, Loc.GetString("VoicesPromoteFailed", voice.Name)),
        };
    }

    private (string key, string region) GetEngineCredentials(string engineId)
    {
        var setting = _owner.CloudEngines.FirstOrDefault(
            s => s.Id.Equals(engineId, StringComparison.OrdinalIgnoreCase));
        return (setting?.ApiKey ?? "", setting?.Region ?? "");
    }

    private void UpdateCounts()
    {
        TotalCount = AllVoices.Count;
        SelectedCount = AllVoices.Count(v => v.IsSelected);
        InstalledCount = AllVoices.Count(v => v.IsInstalled);
    }

    private void ResetFilterOptions()
    {
        EngineOptions.Clear();
        EngineOptions.Add(AllEnginesLabel);
        LanguageOptions.Clear();
        LanguageOptions.Add(AnyLanguageLabel);
        GenderOptions.Clear();
        GenderOptions.Add(AnyGenderLabel);
        GenderOptions.Add("Female");
        GenderOptions.Add("Male");
        QualityOptions.Clear();
        QualityOptions.Add(AnyQualityLabel);

        EngineFilter = AllEnginesLabel;
        LanguageFilter = AnyLanguageLabel;
        GenderFilter = AnyGenderLabel;
        QualityFilter = AnyQualityLabel;
    }

    private void RebuildFilterOptions()
    {
        // Engines
        var engines = AllVoices.Select(v => v.EngineName).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCulture).ToList();
        var prevEngine = EngineFilter;
        EngineOptions.Clear();
        EngineOptions.Add(AllEnginesLabel);
        foreach (var e in engines) EngineOptions.Add(e);
        EngineFilter = prevEngine != null && EngineOptions.Contains(prevEngine) ? prevEngine : AllEnginesLabel;

        // Languages
        var languages = AllVoices.Select(v => v.Language).Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCulture).ToList();
        var prevLang = LanguageFilter;
        LanguageOptions.Clear();
        LanguageOptions.Add(AnyLanguageLabel);
        foreach (var l in languages) LanguageOptions.Add(l);
        LanguageFilter = prevLang != null && LanguageOptions.Contains(prevLang) ? prevLang : AnyLanguageLabel;

        // Quality tiers
        var qualities = AllVoices.Select(v => string.IsNullOrEmpty(v.Quality) ? "unknown" : v.Quality)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(q => q, StringComparer.CurrentCulture).ToList();
        var prevQuality = QualityFilter;
        QualityOptions.Clear();
        QualityOptions.Add(AnyQualityLabel);
        foreach (var q in qualities) QualityOptions.Add(q);
        QualityFilter = prevQuality != null && QualityOptions.Contains(prevQuality) ? prevQuality : AnyQualityLabel;

        // Gender options are static (Any/Female/Male).
    }

    private void ApplyFilter()
    {
        var search = SearchText?.Trim() ?? "";
        var engineActive = EngineFilter is { Length: > 0 } && EngineFilter != AllEnginesLabel;
        var languageActive = LanguageFilter is { Length: > 0 } && LanguageFilter != AnyLanguageLabel;
        var genderActive = GenderFilter is { Length: > 0 } && GenderFilter != AnyGenderLabel;
        var qualityActive = QualityFilter is { Length: > 0 } && QualityFilter != AnyQualityLabel;

        FilteredVoices.Clear();
        IEnumerable<VoiceEntry> query = AllVoices;

        if (InstalledOnly) query = query.Where(v => v.IsInstalled);

        if (engineActive) query = query.Where(v => string.Equals(v.EngineName, EngineFilter, StringComparison.OrdinalIgnoreCase));
        if (languageActive) query = query.Where(v => string.Equals(v.Language, LanguageFilter, StringComparison.OrdinalIgnoreCase));
        if (genderActive) query = query.Where(v => string.Equals(v.Gender, GenderFilter, StringComparison.OrdinalIgnoreCase));
        if (qualityActive)
        {
            var wanted = QualityFilter!;
            query = query.Where(v =>
            {
                var tier = string.IsNullOrEmpty(v.Quality) ? "unknown" : v.Quality;
                return string.Equals(tier, wanted, StringComparison.OrdinalIgnoreCase);
            });
        }
        if (search.Length > 0)
        {
            query = query.Where(v =>
                v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Language.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.EngineName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var v in query) FilteredVoices.Add(v);
    }
}
