using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty] private bool isDownloaded;
    [ObservableProperty] private bool isPromoted;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private int downloadProgress;
    [ObservableProperty] private string downloadStatus = "";
}

public partial class SherpaModelsViewModel : ObservableObject
{
    [ObservableProperty] private string searchFilter = "";
    [ObservableProperty] private string languageFilter = "";
    [ObservableProperty] private bool showInstalledOnly;
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int downloadedCount;
    [ObservableProperty] private int promotedCount;

    public ObservableCollection<SherpaModelItem> AllModels { get; } = new();
    public ObservableCollection<SherpaModelItem> FilteredModels { get; } = new();

    private List<SherpaModelService.CatalogModel> _catalog = new();
    private List<SherpaModelService.InstalledModel> _installed = new();

    partial void OnSearchFilterChanged(string value) => ApplyFilter();
    partial void OnLanguageFilterChanged(string value) => ApplyFilter();
    partial void OnShowInstalledOnlyChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadCatalog()
    {
        IsLoading = true;
        StatusText = "Loading catalog...";

        try
        {
            _catalog = await SherpaModelService.LoadCatalogAsync();
            RefreshInstalled();
            PopulateModels();
            StatusText = $"Loaded {_catalog.Count} models, {_installed.Count} installed";
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
        StatusText = $"Rescanned: {_installed.Count} models installed";
    }

    [RelayCommand]
    private async Task DownloadSelected()
    {
        var selected = AllModels.Where(m => m.IsSelected && !m.IsDownloaded).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Select models to download";
            return;
        }

        foreach (var model in selected)
        {
            var catalogModel = _catalog.FirstOrDefault(c => c.Id == model.Id);
            if (catalogModel == null) continue;

            model.DownloadStatus = "Downloading...";
            try
            {
                var progress = new Progress<(int pct, string msg)>(p =>
                {
                    model.DownloadProgress = p.pct;
                    model.DownloadStatus = p.msg;
                });

                await SherpaModelService.DownloadModelAsync(catalogModel, (IProgress<(int, string)>)progress);
                model.IsDownloaded = true;
                model.DownloadStatus = "Downloaded";
            }
            catch (Exception ex)
            {
                model.DownloadStatus = $"Failed: {ex.Message}";
            }
        }

        Rescan();
    }

    [RelayCommand]
    private void PromoteAll()
    {
        IsLoading = true;
        StatusText = "Promoting models to HKLM...";

        try
        {
            var (promoted, failed) = SherpaModelService.PromoteAll();
            Rescan();
            StatusText = failed == 0
                ? $"Promoted {promoted} model(s) to HKLM"
                : $"Promoted {promoted}, failed {failed}";
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
    private void OpenModelsFolder()
    {
        var dir = SherpaModelService.GetModelsDir();
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
    }

    private void RefreshInstalled()
    {
        _installed = SherpaModelService.ScanInstalledModels();
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
                Language = cat.Language?.FirstOrDefault()?.Display ?? cat.Language?.FirstOrDefault()?.LangCode ?? "Unknown",
                ModelType = cat.ModelType ?? "vits",
                FileSizeMb = (long)(cat.FileSizeMb ?? 0),
                Url = cat.Url ?? "",
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

    private void ApplyFilter()
    {
        FilteredModels.Clear();
        var filter = SearchFilter?.Trim().ToLowerInvariant() ?? "";
        var langFilter = LanguageFilter?.Trim().ToLowerInvariant() ?? "";

        foreach (var m in AllModels)
        {
            if (ShowInstalledOnly && !m.IsDownloaded) continue;
            if (!string.IsNullOrEmpty(filter) &&
                !m.Name.ToLowerInvariant().Contains(filter) &&
                !m.Id.ToLowerInvariant().Contains(filter) &&
                !m.Language.ToLowerInvariant().Contains(filter)) continue;
            if (!string.IsNullOrEmpty(langFilter) &&
                !m.Language.ToLowerInvariant().Contains(langFilter)) continue;
            FilteredModels.Add(m);
        }
    }

    private void UpdateCounts()
    {
        TotalCount = AllModels.Count;
        DownloadedCount = AllModels.Count(m => m.IsDownloaded);
        PromotedCount = AllModels.Count(m => m.IsPromoted);
    }
}
