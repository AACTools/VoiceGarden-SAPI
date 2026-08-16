using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustTtsWrapper;
using VoiceGarden.UI.Localization;
using VoiceGarden.UI.Services;

namespace VoiceGarden.UI.ViewModels;

public partial class SherpaModelItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Language { get; set; } = "";
    public string ModelType { get; set; } = "vits";
    public long FileSizeMb { get; set; }
    public string Url { get; set; } = "";
    public int SampleRate { get; set; } = 24000;
    public string License { get; set; } = "";
    public string LicenseUrl { get; set; } = "";
    public string MinSherpaOnnxVersion { get; set; } = "";
    public bool IsDeprecated { get; set; }
    public string Quality { get; set; } = "";
    public string Gender { get; set; } = "";

    [ObservableProperty] private bool isDownloaded;
    [ObservableProperty] private bool isPromoted;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isDownloading;
    [ObservableProperty] private int downloadProgress;
    [ObservableProperty] private string downloadStatus = "";

    public string QualityBadge => string.IsNullOrEmpty(Quality) || Quality == "unknown" ? "" : Quality;

    public string GenderLabel => Gender is "Male" or "Female" ? Gender : "";

    public string DetailsToolTip
    {
        get
        {
            var parts = new List<string> { $"{Id} ({ModelType})" };
            if (FileSizeMb > 0) parts.Add($"{FileSizeMb:F0} MB");
            if (SampleRate > 0) parts.Add($"{SampleRate / 1000.0:F1} kHz");
            if (!string.IsNullOrEmpty(QualityBadge)) parts.Add($"Quality: {Quality}");
            if (!string.IsNullOrEmpty(GenderLabel)) parts.Add($"Voice: {Gender}");
            if (!string.IsNullOrEmpty(License)) parts.Add($"Licence: {License}");
            if (!string.IsNullOrEmpty(MinSherpaOnnxVersion)) parts.Add($"Needs sherpa-onnx {MinSherpaOnnxVersion}+");
            if (IsDeprecated) parts.Add("Deprecated upstream — inference keeps working");
            return string.Join(" | ", parts);
        }
    }
}

public partial class SherpaModelsViewModel : ObservableObject
{
    [ObservableProperty] private string searchFilter = "";
    [ObservableProperty] private string languageFilter = "";
    [ObservableProperty] private bool showInstalledOnly;
    [ObservableProperty] private string qualityFilter = "";  // "", high, medium, low, x_low, int8, fp16, unknown
    [ObservableProperty] private string statusText = Loc.GetString("Ready");
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int downloadedCount;
    [ObservableProperty] private int promotedCount;

    public IReadOnlyList<string> QualityTiers { get; } = new[] { "", "high", "medium", "low", "x_low", "int8", "fp16", "unknown" };

    public ObservableCollection<SherpaModelItem> AllModels { get; } = new();
    public ObservableCollection<SherpaModelItem> FilteredModels { get; } = new();

    private List<SherpaModelService.CatalogModel> _catalog = new();
    private List<SherpaModelService.InstalledModel> _installed = new();

    partial void OnSearchFilterChanged(string value) => ApplyFilter();
    partial void OnLanguageFilterChanged(string value) => ApplyFilter();
    partial void OnShowInstalledOnlyChanged(bool value) => ApplyFilter();
    partial void OnQualityFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadCatalog()
    {
        IsLoading = true;
        StatusText = Loc.GetString("LoadingCatalog");

        try
        {
            // Load catalog off the UI thread to prevent freezing with 1300+ models
            var catalog = await Task.Run(() => SherpaModelService.LoadCatalogAsync().GetAwaiter().GetResult());

            // fp16 builds SIGABRT the wrapper's CPU-only ONNX runtime (Rust
            // cannot catch the foreign exception) — hide them from the
            // downloadable list.
            var fp16Hidden = catalog.Count(c => c.Url.Contains("fp16", StringComparison.OrdinalIgnoreCase));
            catalog = catalog.Where(c => !c.Url.Contains("fp16", StringComparison.OrdinalIgnoreCase)).ToList();

            var installed = await Task.Run(() => SherpaModelService.ScanInstalledModels());
            _installed = installed;

            // Build model items off the UI thread
            var items = await Task.Run(() =>
            {
                var result = new System.Collections.Concurrent.ConcurrentBag<SherpaModelItem>();
                System.Threading.Tasks.Parallel.ForEach(catalog, cat =>
                {
                    var langInfo = cat.Language?.FirstOrDefault();
                    var inst = installed.FirstOrDefault(i => i.Id == cat.Id);
                    result.Add(new SherpaModelItem
                    {
                        Id = cat.Id,
                        Name = string.IsNullOrEmpty(cat.Name) ? cat.Id : cat.Name,
                        Language = langInfo?.LanguageName ?? "Unknown",
                        ModelType = cat.ModelType?.Contains("kokoro") == true ? "kokoro"
                                 : cat.ModelType?.Contains("matcha") == true ? "matcha"
                                 : "vits",
                        Url = cat.Url ?? "",
                        FileSizeMb = (long)(cat.FileSizeMb ?? 0),
                        SampleRate = cat.SampleRate ?? 24000,
                        License = cat.License ?? "",
                        LicenseUrl = cat.LicenseUrl ?? "",
                        MinSherpaOnnxVersion = cat.MinSherpaOnnxVersion ?? "",
                        IsDeprecated = cat.Deprecated ?? false,
                        Quality = cat.Quality ?? "",
                        Gender = SherpaModelService.DeriveSherpaGender(cat.Id, cat.Name, cat.NumSpeakers ?? 1),
                        IsDownloaded = inst != null,
                        IsPromoted = inst?.IsPromoted ?? false,
                    });
                });
                return result.ToList();
            });

            AllModels.Clear();
            foreach (var item in items)
                AllModels.Add(item);

            ApplyFilter();
            UpdateCounts();
            StatusText = fp16Hidden > 0
                ? $"Loaded {AllModels.Count} voices, {_installed.Count} installed ({fp16Hidden} fp16 models hidden — incompatible with the CPU runtime)"
                : $"Loaded {AllModels.Count} voices, {_installed.Count} installed";
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
        private void Rescan()
        {
            RefreshInstalled();
            foreach (var item in AllModels)
            {
                var installed = _installed.FirstOrDefault(i => i.Id == item.Id);
                item.IsDownloaded = installed != null;
                item.IsPromoted = installed?.IsPromoted ?? false;
            }
            UpdateCounts();
            StatusText = Loc.GetString("RescannedModels", _installed.Count);
        }

    [ObservableProperty] private bool addEnUsAlias = true;

    [RelayCommand]
    private async Task DownloadSelected()
    {
        var selected = AllModels.Where(m => m.IsSelected && !m.IsDownloaded).ToList();
        if (selected.Count == 0)
        {
            StatusText = Loc.GetString("SelectModelsFirst");
            return;
        }

        // Load catalog for download URLs
        var catalog = await SherpaModelService.LoadCatalogAsync();

        int okCount = 0, failCount = 0;

        foreach (var model in selected)
        {
            var catalogModel = catalog.FirstOrDefault(c => c.Id == model.Id);
            if (catalogModel == null || string.IsNullOrEmpty(catalogModel.Url))
            {
                model.DownloadStatus = Loc.GetString("NoDownloadUrl");
                failCount++;
                continue;
            }

            var sizeMb = catalogModel.FileSizeMb > 0 ? $"{catalogModel.FileSizeMb:F0}MB" : "??MB";
            model.DownloadStatus = Loc.GetString("DownloadingModel", model.Id, sizeMb);
            model.DownloadProgress = 0;
            model.IsDownloading = true;
            StatusText = Loc.GetString("DownloadingModel", model.Name, sizeMb);

            try
            {
                var progress = new Progress<(int pct, string msg)>(p =>
                {
                    model.DownloadProgress = p.pct;
                    model.DownloadStatus = $"{p.pct}% - {model.Id}";
                    StatusText = $"Downloading {model.Name}: {p.msg}";
                });

                await SherpaModelService.DownloadModelAsync(catalogModel, (IProgress<(int, string)>)progress);
                model.IsDownloaded = true;
                model.IsDownloading = false;
                model.DownloadProgress = 100;
                model.DownloadStatus = Loc.GetString("DownloadedStatus");
            }
            catch (Exception ex)
            {
                model.IsDownloading = false;
                model.DownloadProgress = 0;
                var msg = ex.Message;
                // Unwrap inner exceptions for SSL/TLS errors
                if (ex.InnerException != null)
                    msg += $" → {ex.InnerException.Message}";
                model.DownloadStatus = $"Failed: {msg}";
                failCount++;
            }
        }

        RefreshInstalled();
        foreach (var item in AllModels)
        {
            var installed = _installed.FirstOrDefault(i => i.Id == item.Id);
            item.IsDownloaded = installed != null;
            item.IsPromoted = installed?.IsPromoted ?? false;
        }
        UpdateCounts();

        if (okCount > 0)
            Services.AnalyticsService.Track("model_downloaded", ("count", okCount), ("failed", failCount));

        StatusText = failCount == 0
            ? Loc.GetString("DownloadedCount", okCount)
            : okCount > 0
                ? Loc.GetString("DownloadedFailedCount", okCount, failCount)
                : Loc.GetString("DownloadFailed", failCount);
    }

    [RelayCommand]
    private void PromoteAll()
    {
        IsLoading = true;
        StatusText = Loc.GetString("InstallingSAPI");

        try
        {
            // Try direct first (works if already admin)
            var (promoted, failed) = SherpaModelService.PromoteAll(AddEnUsAlias);

            // Nothing promoted AND nothing failed means no .onnx models were found at all
            if (promoted == 0 && failed == 0)
            {
                Rescan();
                StatusText = Loc.GetString("NoDownloadedModels");
                IsLoading = false;
                return;
            }

            if (promoted > 0)
            {
                // Succeeded (running as admin)
                Rescan();
                StatusText = failed == 0
                    ? Loc.GetString("InstalledModelsSAPI", promoted)
                    : Loc.GetString("InstalledModelsFailed", promoted, failed);
                return;
            }

            // promoted == 0 && failed > 0 — need elevation.
            // Use the fast .reg import path instead of relaunching the exe.
            StatusText = Loc.GetString("RequestingAdmin");
            var (elevPromoted, elevFailed, error) = SherpaModelService.PromoteAllElevated();

            Rescan();

            if (elevPromoted > 0)
            {
                Services.AnalyticsService.Track("voices_promoted", ("engine", "sherpaonnx"), ("count", elevPromoted));
                StatusText = elevFailed == 0
                    ? Loc.GetString("InstalledModelsSAPI", elevPromoted)
                    : Loc.GetString("InstalledModelsFailed", elevPromoted, elevFailed);
            }
            else if (error == "UAC cancelled")
            {
                StatusText = Loc.GetString("InstallCancelled");
            }
            else
            {
                StatusText = Loc.GetString("InstallFailed", error);
            }
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
    private async Task PreviewModel(SherpaModelItem model)
    {
        if (!model.IsDownloaded)
        {
            StatusText = Loc.GetString("DownloadModelFirst");
            return;
        }

        model.DownloadStatus = Loc.GetString("Previewing");
        try
        {
            var installed = _installed.FirstOrDefault(i => i.Id == model.Id);
            if (installed?.ModelPath == null)
            {
                StatusText = Loc.GetString("ModelFilesNotFound");
                return;
            }

            // Use rust-tts-wrapper for preview (same engine as the SAPI adapter)
            // Derive modelId and modelPath from the installed model path
            var modelId = "";
            var modelBasePath = "";
            if (installed.ModelPath != null)
            {
                var p = System.IO.Path.GetDirectoryName(installed.ModelPath);
                while (p != null && System.IO.Path.GetFileName(p) != "models")
                    p = System.IO.Path.GetDirectoryName(p);
                if (p != null && System.IO.Path.GetFileName(p) == "models")
                {
                    var rel = System.IO.Path.GetRelativePath(p, System.IO.Path.GetDirectoryName(installed.ModelPath)!);
                    modelId = rel.Split(System.IO.Path.DirectorySeparatorChar)[0];
                    modelBasePath = p;
                }
            }

            if (string.IsNullOrEmpty(modelId))
            {
                StatusText = Loc.GetString("NoModelId");
                return;
            }

            using var client = new RustTtsWrapper.TtsClient("sherpaonnx", new Dictionary<string, string>
            {
                { "modelId", modelId },
                { "modelPath", modelBasePath }
            });

            // Use language-appropriate preview text
            StatusText = $"Previewing {model.Name}...";
            var audioData = client.SynthToBytes(AudioPreview.GetSherpaPreviewText(model.Id, model.Name));
            if (audioData.Length > 0)
            {
                // Rust returns raw PCM16 mono — wrap in WAV and play.
                // Use the model's catalog sample rate (not all models are 24 kHz).
                AudioPreview.PlayPcm(audioData, model.SampleRate > 0 ? model.SampleRate : 24000, "vg_sherpa_");
                model.DownloadStatus = Loc.GetString("DownloadedStatus");
                StatusText = $"Previewing {model.Name}";
            }
            else
            {
                model.DownloadStatus = Loc.GetString("DownloadedStatus");
                StatusText = $"No audio generated for {model.Name}";
            }
        }
        catch (RustTtsWrapper.TtsException ex)
        {
            model.DownloadStatus = Loc.GetString("DownloadedStatus");
            StatusText = $"Preview failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Preview exception for {model.Id}: {ex}");
        }
    }

    [RelayCommand]
    private void OpenModelsFolder()
    {
        var dir = SherpaModelService.GetModelsDir();
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
    }

    private void RefreshInstalled()
    {
        _installed = SherpaModelService.ScanInstalledModels()
            .Where(m => !string.IsNullOrEmpty(m.ModelPath))
            .ToList();
    }

    private void PopulateModels()
    {
        AllModels.Clear();
        foreach (var cat in _catalog)
        {
            var installed = _installed.FirstOrDefault(i => i.Id == cat.Id);
            var item = new SherpaModelItem
            {
                Id = cat.Id,
                Name = cat.Name,
                Language = cat.Language?.FirstOrDefault()?.LanguageName ??
                           cat.Language?.FirstOrDefault()?.LangCode ??
                           cat.Language?.FirstOrDefault()?.Country ?? "Unknown",
                ModelType = cat.ModelType ?? "vits",
                FileSizeMb = (long)(cat.FileSizeMb ?? 0),
                Url = cat.Url ?? "",
                SampleRate = cat.SampleRate ?? 24000,
                License = cat.License ?? "",
                LicenseUrl = cat.LicenseUrl ?? "",
                MinSherpaOnnxVersion = cat.MinSherpaOnnxVersion ?? "",
                IsDeprecated = cat.Deprecated ?? false,
                Quality = cat.Quality ?? "",
                Gender = SherpaModelService.DeriveSherpaGender(cat.Id, cat.Name, cat.NumSpeakers ?? 1),
                IsDownloaded = installed != null,
                IsPromoted = installed?.IsPromoted ?? false,
            };
            AllModels.Add(item);
        }
        // Also add installed models not in catalog
        foreach (var inst in _installed)
        {
            if (!AllModels.Any(m => m.Id == inst.Id))
            {
                AllModels.Add(new SherpaModelItem
                {
                    Id = inst.Id,
                    Name = inst.Id,
                    IsDownloaded = true,
                    IsPromoted = inst.IsPromoted,
                });
            }
        }

        ApplyFilter();
        UpdateCounts();
    }

    private static int QualityRank(string q) => q switch
    {
        "high" => 0,
        "medium" => 1,
        "int8" => 2,
        "low" => 3,
        "fp16" => 4,
        "x_low" => 5,
        "" or "unknown" => 7,
        _ => 6,
    };

    private void ApplyFilter()
    {
        FilteredModels.Clear();
        var filter = SearchFilter?.Trim().ToLowerInvariant() ?? "";
        var langFilter = LanguageFilter?.Trim().ToLowerInvariant() ?? "";
        var qualityFilter = QualityFilter ?? "";

        foreach (var m in AllModels)
        {
            if (ShowInstalledOnly && !m.IsDownloaded) continue;
            if (!string.IsNullOrEmpty(qualityFilter))
            {
                var tier = string.IsNullOrEmpty(m.Quality) ? "unknown" : m.Quality;
                if (!string.Equals(tier, qualityFilter, StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (!string.IsNullOrEmpty(filter) &&
                !m.Name.ToLowerInvariant().Contains(filter) &&
                !m.Id.ToLowerInvariant().Contains(filter) &&
                !m.Language.ToLowerInvariant().Contains(filter)) continue;
            if (!string.IsNullOrEmpty(langFilter) &&
                !m.Language.ToLowerInvariant().Contains(langFilter)) continue;
            FilteredModels.Add(m);
        }

        // Sort by quality tier (high -> low, unknown last), stable within tiers
        var sorted = FilteredModels.OrderBy(m => QualityRank(m.Quality))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        FilteredModels.Clear();
        foreach (var m in sorted)
            FilteredModels.Add(m);
    }

    private void UpdateCounts()
    {
        TotalCount = AllModels.Count;
        DownloadedCount = AllModels.Count(m => m.IsDownloaded);
        PromotedCount = AllModels.Count(m => m.IsPromoted);
    }
}
