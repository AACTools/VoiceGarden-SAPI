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
    [ObservableProperty] private string statusText = Loc.GetString("Ready");
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
        StatusText = Loc.GetString("LoadingCatalog");

        try
        {
            // Load catalog off the UI thread to prevent freezing with 1300+ models
            var catalog = await Task.Run(() => SherpaModelService.LoadCatalogAsync().GetAwaiter().GetResult());
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
            var previewText = GetPreviewText(model);
            StatusText = $"Previewing {model.Name}...";
            var audioData = client.SynthToBytes(previewText);
            if (audioData.Length > 0)
            {
                // Rust returns raw PCM16 mono — wrap in WAV header for SoundPlayer
                var wavData = WrapPcmInWav(audioData, 24000);
                var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vg_sherpa_{Guid.NewGuid():N}.wav");
                await System.IO.File.WriteAllBytesAsync(tempFile, wavData);
                _ = Task.Run(() =>
                {
                    try { using var p = new System.Media.SoundPlayer(tempFile); p.PlaySync(); }
                    catch (Exception playEx) { System.Diagnostics.Debug.WriteLine($"SoundPlayer: {playEx.Message}"); }
                    finally { try { System.IO.File.Delete(tempFile); } catch { } }
                });
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

    /// <summary>
    /// Get language-appropriate preview text. MMS models are character-based and
    /// only recognize characters from their target language script.
    /// </summary>
    private static string GetPreviewText(SherpaModelItem model)
    {
        var id = model.Id.ToLowerInvariant();

        // English models — use English
        if (id.Contains("eng") || id.StartsWith("piper-en") || id.StartsWith("kokoro-en"))
            return $"Hello, this is a {model.Name} voice.";

        // MMS models — extract the ISO 639-3 code and try a native greeting
        if (id.StartsWith("mms_"))
        {
            var langCode = id.Substring(4); // e.g., "fas", "hyw", "ara"
            return langCode switch
            {
                "fas" => "سلام، این یک صدای فارسی است.",           // Persian
                "ara" => "مرحبا، هذه تجربة صوتية.",                // Arabic
                "hyw" or "hye" => "Բարև, սա ձայնային փորձարկում է:", // Armenian
                "hin" => "नमस्ते, यह एक आवाज परीक्षण है।",            // Hindi
                "ben" => "হ্যালো, এটি একটি ভয়েস পরীক্ষা।",           // Bengali
                "urd" => "ہیلو، یہ ایک آواز کا ٹیسٹ ہے۔",              // Urdu
                "rus" => "Привет, это тестовое озвучивание.",         // Russian
                "zho" or "cmn" => "你好，这是一个语音测试。",           // Chinese
                "jpn" => "こんにちは、これは音声テストです。",          // Japanese
                "kor" => "안녕하세요, 음성 테스트입니다.",              // Korean
                "tur" => "Merhaba, bu bir ses testidir.",             // Turkish
                "vie" => "Xin chào, đây là một bài kiểm tra giọng nói.", // Vietnamese
                "tha" => "สวัสดีนี่คือการทดสอบเสียงพูด",              // Thai
                "fra" or "fre" => "Bonjour, ceci est un test vocal.", // French
                "deu" or "ger" => "Hallo, dies ist ein Sprachtest.",  // German
                "spa" => "Hola, esta es una prueba de voz.",         // Spanish
                "por" => "Olá, este é um teste de voz.",             // Portuguese
                "ita" => "Ciao, questo è un test vocale.",           // Italian
                "guj" => "નમસ્તે, આ એક અવાજ ચકાસણી છે.",               // Gujarati
                _ => $"[test] {langCode}", // Fallback — may produce no audio
            };
        }

        // Piper/Kokoro non-English — try English (Piper models often support it)
        return $"Hello. {model.Name}.";
    }

    /// <summary>
    /// Wrap raw PCM16 mono samples in a WAV header so SoundPlayer can play them.
    /// </summary>
    private static byte[] WrapPcmInWav(byte[] pcm, int sampleRate)
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        short channels = 1;
        short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + pcm.Length);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(pcm.Length);
        bw.Write(pcm);
        return ms.ToArray();
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
