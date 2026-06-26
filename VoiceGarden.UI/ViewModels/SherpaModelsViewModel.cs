using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetTtsWrapper.Models;
using DotNetTtsWrapper.Engines;
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
    [ObservableProperty] private bool isDownloading;
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
            // Use DotNetTtsWrapper to get the unified voice list with proper BCP-47 codes
            var client = TtsFactory.CreateClient("sherpaonnx", new SherpaOnnxCredentials());
            var voices = await client.GetVoicesAsync();

            AllModels.Clear();
            foreach (var v in voices)
            {
                var langInfo = v.LanguageCodes?.FirstOrDefault();
                var installed = _installed.FirstOrDefault(i => i.Id == v.Id);
                var item = new SherpaModelItem
                {
                    Id = v.Id,
                    Name = v.Description ?? v.Name ?? v.Id,
                    Language = langInfo?.Display ?? langInfo?.Bcp47 ?? "Unknown",
                    ModelType = v.Description?.Contains("kokoro") == true ? "kokoro"
                             : v.Description?.Contains("matcha") == true ? "matcha"
                             : "vits",
                    Url = "", // URL not available from TtsVoice, would need catalog
                    IsDownloaded = installed != null,
                    IsPromoted = installed?.IsPromoted ?? false,
                };
                AllModels.Add(item);
            }

            RefreshInstalled();
            ApplyFilter();
            UpdateCounts();
            StatusText = $"Loaded {AllModels.Count} voices, {_installed.Count} installed";
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

    [ObservableProperty] private bool addEnUsAlias = true;

    [RelayCommand]
    private async Task DownloadSelected()
    {
        var selected = AllModels.Where(m => m.IsSelected && !m.IsDownloaded).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Select models to download first";
            return;
        }

        // Load catalog for download URLs
        var catalog = await SherpaModelService.LoadCatalogAsync();

        foreach (var model in selected)
        {
            var catalogModel = catalog.FirstOrDefault(c => c.Id == model.Id);
            if (catalogModel == null || string.IsNullOrEmpty(catalogModel.Url))
            {
                model.DownloadStatus = "No download URL";
                continue;
            }

            var sizeMb = catalogModel.FileSizeMb > 0 ? $"{catalogModel.FileSizeMb:F0}MB" : "??MB";
            model.DownloadStatus = $"Downloading {model.Id} ({sizeMb})...";
            model.DownloadProgress = 0;
            model.IsDownloading = true;
            StatusText = $"Downloading {model.Name} ({sizeMb})...";

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
                model.DownloadStatus = "Downloaded";
                StatusText = $"Downloaded {model.Name} ({sizeMb})";
            }
            catch (Exception ex)
            {
                model.IsDownloading = false;
                model.DownloadProgress = 0;
                model.DownloadStatus = $"Failed: {ex.Message}";
                StatusText = $"Download failed: {model.Name}";
            }
        }

        Rescan();
        StatusText = "Download complete";
    }

    [RelayCommand]
    private void PromoteAll()
    {
        IsLoading = true;
        StatusText = "Installing models to SAPI (HKLM)...";

        try
        {
            // Try direct first (works if already admin)
            var (promoted, failed) = SherpaModelService.PromoteAll(AddEnUsAlias);

            if (promoted == 0 && failed > 0)
            {
                // Not admin — relaunch elevated via CLI
                StatusText = "Requesting admin privileges...";
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                var psi = new System.Diagnostics.ProcessStartInfo(exePath, "models promote-all")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                };

                try
                {
                    var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit(30000);
                    var rc = p?.ExitCode ?? -1;
                    Rescan();
                    StatusText = rc == 0 ? "Models installed to SAPI (elevated)" : $"Install failed (exit {rc})";
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    StatusText = "Install cancelled (admin permission denied)";
                }
            }
            else
            {
                Rescan();
                StatusText = failed == 0
                    ? $"Installed {promoted} model(s) to SAPI"
                    : $"Installed {promoted}, failed {failed}";
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
            StatusText = "Download the model first";
            return;
        }

        model.DownloadStatus = "Previewing...";
        try
        {
            var installed = _installed.FirstOrDefault(i => i.Id == model.Id);
            if (installed?.ModelPath == null)
            {
                StatusText = "Model files not found";
                return;
            }

            var creds = new DotNetTtsWrapper.Models.SherpaOnnxCredentials
            {
                ModelFilePath = installed.ModelPath,
                TokensFilePath = installed.TokensPath,
                DataDirPath = installed.DataDir,
            };
            var client = DotNetTtsWrapper.Models.TtsFactory.CreateClient("sherpaonnx", creds);
            if (client == null)
            {
                StatusText = "Could not create SherpaOnnx client";
                return;
            }

            client.SetVoice(model.Id);
            var result = await client.SynthToBytesAsync($"Hello, this is a {model.Name} voice.");
            if (result?.AudioData?.Length > 0)
            {
                var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vg_sherpa_{Guid.NewGuid():N}.wav");
                await System.IO.File.WriteAllBytesAsync(tempFile, result.AudioData);
                _ = Task.Run(() =>
                {
                    try { using var p = new System.Media.SoundPlayer(tempFile); p.PlaySync(); }
                    catch { }
                    finally { try { System.IO.File.Delete(tempFile); } catch { } }
                });
                model.DownloadStatus = "Downloaded";
                StatusText = $"Previewing {model.Name}";
            }
            else
            {
                model.DownloadStatus = "Downloaded";
                StatusText = "No audio generated";
            }
        }
        catch (Exception ex)
        {
            model.DownloadStatus = "Downloaded";
            StatusText = $"Preview failed: {ex.Message}";
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
                Language = cat.Language?.FirstOrDefault()?.LanguageName ??
                           cat.Language?.FirstOrDefault()?.LangCode ??
                           cat.Language?.FirstOrDefault()?.Country ?? "Unknown",
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
