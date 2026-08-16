using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Manages SherpaOnnx model catalog, download, and SAPI token promotion.
/// Replaces the functionality of SherpaOnnxConfig.exe.
/// </summary>
public class SherpaModelService
{
    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceGardenSAPIAdapter", "models");

    private const string SapiTokensRoot = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";
    private const string OneCoreTokensRoot = @"SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens";
    private const string TtsEngineClsid = "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}";

    public class CatalogModel
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("model_type")] public string ModelType { get; set; } = "vits";
        [JsonPropertyName("sample_rate")] public int? SampleRate { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("language")] public List<CatalogLanguage>? Language { get; set; }
        [JsonPropertyName("filesize_mb")] public double? FileSizeMb { get; set; }
        [JsonPropertyName("license")] public string? License { get; set; }
        [JsonPropertyName("license_url")] public string? LicenseUrl { get; set; }
        [JsonPropertyName("min_sherpa_onnx_version")] public string? MinSherpaOnnxVersion { get; set; }
        [JsonPropertyName("deprecated")] public bool? Deprecated { get; set; }
        [JsonPropertyName("quality")] public string? Quality { get; set; }
        [JsonPropertyName("num_speakers")] public int? NumSpeakers { get; set; }
    }

    public class CatalogLanguage
    {
        [JsonPropertyName("lang_code")] public string LangCode { get; set; } = "";
        [JsonPropertyName("language_name")] public string LanguageName { get; set; } = "";
        [JsonPropertyName("country")] public string Country { get; set; } = "";
    }

    public class InstalledModel
    {
        public string Id { get; set; } = "";
        public string Directory { get; set; } = "";
        public string? ModelPath { get; set; }
        public string? TokensPath { get; set; }
        public string? DataDir { get; set; }
        public string? VoicesPath { get; set; }
        public string? LexiconPath { get; set; }
        public bool IsPromoted { get; set; }

        /// <summary>
        /// 0=VITS, 1=Matcha, 2=Kokoro
        /// </summary>
        public int ModelType { get; set; } = 0;

        /// <summary>SAPI Gender attribute value: Male/Female/Neutral (Neutral = unknown).</summary>
        public string Gender { get; set; } = "Neutral";

        /// <summary>Registry quality tier (high/medium/low/x_low/int8/fp16), empty when unknown.</summary>
        public string Quality { get; set; } = "";
    }

    /// <summary>
    /// Load the model catalog from models.json (embedded or sidecar).
    /// Falls back to the pre-0.3.17 merged_models.json sidecar if present.
    /// </summary>
    public static async Task<List<CatalogModel>> LoadCatalogAsync()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "models.json"),
            Path.Combine(AppContext.BaseDirectory, "x64", "models.json"),
            Path.Combine(AppContext.BaseDirectory, "x86", "models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SherpaOnnxConfig", "models.json"),
            // Legacy sidecar from wrapper <= 0.3.16 installs
            Path.Combine(AppContext.BaseDirectory, "merged_models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "merged_models.json"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);

                // The catalog is a dict keyed by model ID: { "id": { ... }, ... }
                var dict = JsonSerializer.Deserialize<Dictionary<string, CatalogModel>>(json);
                if (dict != null && dict.Count > 0)
                    return dict.Values.ToList();

                // Fallback: try as array
                var list = JsonSerializer.Deserialize<List<CatalogModel>>(json);
                return list ?? new();
            }
        }

        return new();
    }

    /// <summary>
    /// Legacy -> canonical model IDs from the 2026-08-10 sherpa-onnx registry
    /// canonicalisation. rust-tts-wrapper (>= 0.3.17) hard-fails on unknown
    /// model IDs, so installed directories using legacy names are renamed on
    /// scan (idempotent; the SAPI adapter performs the same migration).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyModelIds = new Dictionary<string, string>
    {
        ["kokoro-en-en-19"] = "kokoro-en-v0_19",
        ["kokoro-zh_en-int8-multi"] = "kokoro-zh_en-int8",
        ["vits-coqui-en-vctk"] = "coqui-en-vctk",
        ["tts-fs-khadijah"] = "matcha-fs-khadijah",
        ["tts-fs-musa"] = "matcha-fs-musa",
        ["mimic3-af-google-nwu_low"] = "mimic3-af-google-low",
        ["mimic3-bn-multi"] = "mimic3-bn-multi_low",
        ["mimic3-el-rapunzelina"] = "mimic3-el-rapunzelina_low",
        ["mimic3-es-m-ailabs_low"] = "mimic3-es-m-low",
        ["mimic3-fa-haaniye"] = "mimic3-fa-haaniye_low",
        ["mimic3-ko-kss"] = "mimic3-ko-kss_low",
        ["mimic3-pl-m-ailabs_low"] = "mimic3-pl-m-low",
        ["mimic3-tn-google-nwu_low"] = "mimic3-tn-google-low",
        ["mimic3-vi-vais1000"] = "mimic3-vi-vais1000_low",
    };

    /// <summary>
    /// Rename installed model directories still using legacy registry IDs to
    /// their canonical names, so the wrapper's registry lookups succeed and
    /// the catalog matches installed models. Best-effort; locked or in-use
    /// directories are left for the adapter's migration to retry.
    /// </summary>
    private static void MigrateLegacyModelDirs()
    {
        if (!Directory.Exists(ModelsDir)) return;

        foreach (var (legacy, canonical) in LegacyModelIds)
        {
            var legacyDir = Path.Combine(ModelsDir, legacy);
            var canonicalDir = Path.Combine(ModelsDir, canonical);
            if (!Directory.Exists(legacyDir) || Directory.Exists(canonicalDir))
                continue;

            try
            {
                Directory.Move(legacyDir, canonicalDir);
            }
            catch
            {
                // In use or locked — the SAPI adapter retries on voice init.
            }
        }
    }

    /// <summary>
    /// Derive a SAPI Gender attribute (Male/Female/Neutral) for a sherpa model.
    /// The registry carries no gender field, so this uses naming conventions:
    /// - word tokens female/woman/girl (checked first — "female" contains "male")
    ///   and male/man/boy in the id or display name;
    /// - the piper ecosystem's af_/am_ (adult female/male) underscore prefixes;
    /// - mimic3's single-letter m/f (m-ailabs / f-ailabs) variants.
    /// Multi-speaker models and the MMS family (whose ids are language codes and
    /// whose names are language names, e.g. the "Male" language of Ethiopia)
    /// are left Neutral rather than guessed.
    /// </summary>
    public static string DeriveSherpaGender(string id, string name, int numSpeakers)
    {
        if (numSpeakers > 1) return "Neutral";
        var lower = (id + " " + name).ToLowerInvariant();
        if (lower.StartsWith("mms_") || lower.Contains(" mms_")) return "Neutral";

        // Piper af_/am_ (e.g. hand-installed af_amy, am_adam voices). The
        // underscore form matters: "af-" / "am-" segments are Afrikaans /
        // Armenian language codes, not gender markers.
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"(?:^|[^a-z0-9])af_[a-z]")) return "Female";
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"(?:^|[^a-z0-9])am_[a-z]")) return "Male";

        var tokens = lower.Split(new[] { ' ', '-', '_', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var isFemale = false;
        var isMale = false;
        var hasSingleM = false;
        var hasSingleF = false;
        foreach (var tok in tokens)
        {
            switch (tok)
            {
                case "female":
                case "woman":
                case "girl":
                case "women":
                    isFemale = true;
                    break;
                case "male":
                case "man":
                case "boy":
                case "men":
                    isMale = true;
                    break;
                case "m":
                    hasSingleM = true;
                    break;
                case "f":
                    hasSingleF = true;
                    break;
            }
        }

        if (isFemale) return "Female";
        if (isMale) return "Male";
        // mimic3 m-ailabs (male) / f-ailabs (female) variants
        if (lower.StartsWith("mimic3-"))
        {
            if (hasSingleF) return "Female";
            if (hasSingleM) return "Male";
        }
        return "Neutral";
    }

    /// <summary>
    /// Fill Gender/Quality on installed models from the bundled catalog
    /// (single load), so promotion can write them into the SAPI token
    /// attributes. Models missing from the catalog keep Neutral/empty.
    /// </summary>
    private static void EnrichWithCatalog(List<InstalledModel> models)
    {
        try
        {
            var catalog = LoadCatalogAsync().GetAwaiter().GetResult()
                .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var model in models)
            {
                if (!catalog.TryGetValue(model.Id, out var cat)) continue;
                model.Quality = cat.Quality ?? "";
                model.Gender = DeriveSherpaGender(cat.Id, cat.Name, cat.NumSpeakers ?? 1);
            }
        }
        catch
        {
            // Catalog unavailable — promote without gender/quality metadata.
        }
    }

    /// <summary>
    /// Scan the local models directory for installed models.
    /// </summary>
    public static List<InstalledModel> ScanInstalledModels()
    {
        var result = new List<InstalledModel>();
        if (!Directory.Exists(ModelsDir)) return result;

        MigrateLegacyModelDirs();

        // Check which are already promoted to HKLM
        var promoted = GetPromotedSherpaTokens();

        foreach (var dir in Directory.GetDirectories(ModelsDir))
        {
            var modelId = Path.GetFileName(dir);
            // A renamed directory may still be referenced by its legacy token name
            var legacyName = LegacyModelIds.FirstOrDefault(kv => kv.Value == modelId).Key;

            // Auto-extract any orphaned .tar.bz2 left from a failed/aborted extraction
            TryExtractArchives(dir);

            var installed = new InstalledModel
            {
                Id = modelId,
                Directory = dir,
                IsPromoted = promoted.Contains($"Sherpa-{modelId}")
                    || (legacyName != null && promoted.Contains($"Sherpa-{legacyName}")),
            };

            // Find model.onnx (could be in nested dir for Piper)
            var onnxFiles = System.IO.Directory.GetFiles(dir, "*.onnx", SearchOption.AllDirectories);
            if (onnxFiles.Length > 0)
            {
                // Prefer model.onnx over other names
                installed.ModelPath = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("model.onnx", StringComparison.OrdinalIgnoreCase))
                    ?? onnxFiles[0];
                var modelDir = Path.GetDirectoryName(installed.ModelPath)!;

                var tokensPath = Path.Combine(modelDir, "tokens.txt");
                if (File.Exists(tokensPath))
                    installed.TokensPath = tokensPath;

                var dataDir = Path.Combine(modelDir, "espeak-ng-data");
                if (Directory.Exists(dataDir))
                    installed.DataDir = dataDir;

                var voicesPath = Path.Combine(modelDir, "voices.bin");
                if (File.Exists(voicesPath))
                    installed.VoicesPath = voicesPath;

                var lexiconPath = Path.Combine(modelDir, "lexicon.txt");
                if (File.Exists(lexiconPath))
                    installed.LexiconPath = lexiconPath;

                // Detect model type: Kokoro has voices.bin, Matcha has vocoder.onnx
                if (installed.VoicesPath != null || modelId.StartsWith("kokoro-"))
                    installed.ModelType = 2; // Kokoro
                else if (onnxFiles.Any(f => Path.GetFileName(f).Contains("vocoder")))
                    installed.ModelType = 1; // Matcha
                else
                    installed.ModelType = 0; // VITS
            }

            result.Add(installed);
        }

        return result;
    }

    /// <summary>
    /// If a .tar.bz2 or .tar exists in the directory but no .onnx is present yet,
    /// extract it. Self-heals downloads that completed but never extracted.
    /// Uses built-in SharpCompress — no 7-Zip dependency.
    /// </summary>
    private static void TryExtractArchives(string dir)
    {
        // Already extracted — has an onnx file
        if (Directory.GetFiles(dir, "*.onnx", SearchOption.AllDirectories).Length > 0)
        {
            // Clean up any leftover archives from a partial extraction
            foreach (var f in Directory.GetFiles(dir, "*.tar.bz2", SearchOption.TopDirectoryOnly))
                TryDelete(f);
            foreach (var f in Directory.GetFiles(dir, "*.tar", SearchOption.TopDirectoryOnly))
                TryDelete(f);
            return;
        }

        var bz2 = Directory.GetFiles(dir, "*.tar.bz2", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (bz2 != null)
        {
            var tarFile = bz2.Replace(".tar.bz2", ".tar");
            // Stage 1: bz2 → tar using SharpCompress
            try { ExtractBz2(bz2, tarFile); } catch { return; }
            // Stage 2: tar → contents
            if (File.Exists(tarFile))
            {
                try { ExtractTar(tarFile, dir); } catch { }
                TryDelete(tarFile);
            }
            TryDelete(bz2);
        }
        else
        {
            // Lone .tar
            var tar = Directory.GetFiles(dir, "*.tar", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (tar != null)
            {
                try { ExtractTar(tar, dir); } catch { }
                TryDelete(tar);
            }
        }
    }

    /// <summary>Extract a .bz2 file to an output file using SharpCompress.</summary>
    private static void ExtractBz2(string bz2Path, string outputPath)
    {
        using var input = File.OpenRead(bz2Path);
        using var decompressor = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
            input, SharpCompress.Compressors.CompressionMode.Decompress, false, false);
        using var output = File.Create(outputPath);
        decompressor.CopyTo(output);
    }

    /// <summary>Extract a .tar archive to a directory using SharpCompress.</summary>
    private static void ExtractTar(string tarPath, string destDir)
    {
        using var archive = SharpCompress.Archives.Tar.TarArchive.OpenArchive(tarPath);
        foreach (var entry in archive.Entries)
        {
            if (!entry.IsDirectory)
            {
                using var entryStream = entry.OpenEntryStream();
                var fullPath = Path.Combine(destDir, entry.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var fileStream = File.Create(fullPath);
                entryStream.CopyTo(fileStream);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void SafeDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    /// <summary>
    /// Download a model from the catalog URL.
    /// Handles two URL patterns:
    ///   1. Archive URL (ends in .tar.bz2 or .tar) — download + extract with 7-Zip
    ///   2. HuggingFace directory URL (no file extension) — download individual files
    ///      (model.onnx, tokens.txt) from that directory. Used by MMS models.
    /// </summary>
    public static async Task DownloadModelAsync(CatalogModel model, IProgress<(int percent, string status)>? progress = null)
    {
        if (string.IsNullOrEmpty(model.Url))
            throw new InvalidOperationException($"Model {model.Id} has no download URL");

        var destDir = Path.Combine(ModelsDir, model.Id);
        var lastSegment = model.Url.Split('/').Last();

        // Route 1: HuggingFace directory (MMS models) — no archive extension
        var isArchive = lastSegment.EndsWith(".tar.bz2") || lastSegment.EndsWith(".tar");
        if (!isArchive)
        {
            await DownloadHfDirectoryAsync(model.Url, destDir, model.Id, progress);
            return;
        }

        // Route 2: Single archive download + extract
        var destFile = Path.Combine(destDir, lastSegment);
        progress?.Report((0, $"Connecting to {lastSegment}..."));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            SafeDeleteDir(destDir);
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.StatusCode} for {lastSegment}");
        }

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var totalMb = totalBytes > 0 ? totalBytes / (1024.0 * 1024.0) : 0;

        Directory.CreateDirectory(destDir);
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(destFile);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;
        var lastReport = DateTime.UtcNow;

        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;
            if (totalBytes > 0)
            {
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 250 || bytesRead == totalBytes)
                {
                    lastReport = now;
                    var pct = (int)(bytesRead * 100 / totalBytes);
                    var doneMb = bytesRead / (1024.0 * 1024.0);
                    progress?.Report((pct, $"{pct}% ({doneMb:F0}/{totalMb:F0}MB)"));
                }
            }
            else
            {
                var doneMb = bytesRead / (1024.0 * 1024.0);
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 500)
                {
                    lastReport = now;
                    progress?.Report((0, $"{doneMb:F0}MB downloaded"));
                }
            }
        }

        // Extract the archive using built-in SharpCompress (no 7-Zip needed)
        progress?.Report((100, "Extracting..."));
        if (lastSegment.EndsWith(".tar.bz2"))
        {
            var tarFile = destFile.Replace(".tar.bz2", ".tar");
            ExtractBz2(destFile, tarFile);
            if (File.Exists(tarFile))
                ExtractTar(tarFile, destDir);
            TryDelete(tarFile);
        }
        else if (lastSegment.EndsWith(".tar"))
        {
            ExtractTar(destFile, destDir);
        }
        TryDelete(destFile);

        progress?.Report((100, "Done"));
    }

    /// <summary>
    /// Download individual files from a HuggingFace directory URL.
    /// MMS models are stored as directories with model.onnx, tokens.txt, etc.
    /// </summary>
    private static async Task DownloadHfDirectoryAsync(string baseUrl, string destDir, string modelId,
        IProgress<(int percent, string status)>? progress)
    {
        // Files to download for MMS models (in priority order)
        var files = new[] { "model.onnx", "tokens.txt", "lexicon.txt", "espeak-ng-data" };

        Directory.CreateDirectory(destDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        // First, probe which files exist by trying to download model.onnx (required)
        var modelUrl = $"{baseUrl}/model.onnx";
        progress?.Report((0, $"Connecting to {modelId}/model.onnx..."));

        using var modelResp = await http.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!modelResp.IsSuccessStatusCode)
        {
            SafeDeleteDir(destDir);
            throw new HttpRequestException(
                $"HTTP {(int)modelResp.StatusCode} {modelResp.StatusCode} for {modelId}/model.onnx");
        }

        // Download model.onnx with progress (this is the big file)
        await DownloadFileWithProgressAsync(http, modelResp, Path.Combine(destDir, "model.onnx"),
            "model.onnx", progress);

        // Download tokens.txt (required for MMS)
        var tokensUrl = $"{baseUrl}/tokens.txt";
        progress?.Report((100, "Downloading tokens.txt..."));
        try
        {
            await DownloadFileAsync(http, tokensUrl, Path.Combine(destDir, "tokens.txt"));
        }
        catch
        {
            // tokens.txt might not exist for all models — non-fatal
        }

        // Try optional files: lexicon.txt
        foreach (var optFile in new[] { "lexicon.txt" })
        {
            try
            {
                progress?.Report((100, $"Checking {optFile}..."));
                await DownloadFileAsync(http, $"{baseUrl}/{optFile}", Path.Combine(destDir, optFile));
            }
            catch { /* optional */ }
        }

        progress?.Report((100, "Done"));
    }

    private static async Task DownloadFileWithProgressAsync(
        HttpClient http, HttpResponseMessage response, string destPath, string fileName,
        IProgress<(int percent, string status)>? progress)
    {
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var totalMb = totalBytes > 0 ? totalBytes / (1024.0 * 1024.0) : 0;

        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(destPath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;
        var lastReport = DateTime.UtcNow;

        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;
            if (totalBytes > 0)
            {
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 250 || bytesRead == totalBytes)
                {
                    lastReport = now;
                    var pct = (int)(bytesRead * 100 / totalBytes);
                    var doneMb = bytesRead / (1024.0 * 1024.0);
                    progress?.Report((pct, $"{pct}% ({doneMb:F0}/{totalMb:F0}MB)"));
                }
            }
        }
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string destPath)
    {
        using var resp = await http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var file = File.Create(destPath);
        await stream.CopyToAsync(file);
    }

    /// <summary>
    /// Promote all downloaded models to HKLM as SAPI tokens.
    /// </summary>
    public static (int promoted, int failed) PromoteAll(bool compatEnUs = false)
    {
        var models = ScanInstalledModels();
        EnrichWithCatalog(models);
        int promoted = 0, failed = 0;
        var errors = new List<string>();

        foreach (var model in models.Where(m => m.ModelPath != null))
        {
            try
            {
                PromoteSherpaModel(model);
                promoted++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{model.Id}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "VoiceGarden_promote_errors.log");
                File.WriteAllLines(logPath, errors);
            }
            catch { }
        }

        return (promoted, failed);
    }

    /// <summary>
    /// Generate a .reg file for all installed models and import it elevated.
    /// Much faster than relaunching the 116MB single-file exe.
    /// Returns (promoted, failed, errorMessage).
    /// </summary>
    public static (int promoted, int failed, string error) PromoteAllElevated()
    {
        var models = ScanInstalledModels().Where(m => m.ModelPath != null).ToList();
        if (models.Count == 0)
            return (0, 0, "No downloaded models found with a valid model.onnx");

        EnrichWithCatalog(models);

        // Generate .reg file in a shared location (C:\ProgramData) so the elevated
        // process (which may run as a different admin user) can read it.
        var regDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VoiceGardenSAPIAdapter");
        Directory.CreateDirectory(regDir);
        var regPath = Path.Combine(regDir, "promote.reg");
        var lines = new List<string> { "Windows Registry Editor Version 5.00", "" };

        foreach (var model in models)
        {
            AppendModelToReg(lines, model);
        }

        File.WriteAllLines(regPath, lines);

        // Import with reg.exe elevated
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", $"import \"{regPath}\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(30000);
            var rc = p?.ExitCode ?? -1;

            TryDelete(regPath);

            if (rc == 0)
                return (models.Count, 0, "");
            return (0, models.Count, $"reg import exited with code {rc}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            TryDelete(regPath);
            return (0, 0, "UAC cancelled");
        }
        catch (Exception ex)
        {
            TryDelete(regPath);
            return (0, models.Count, ex.Message);
        }
    }

    private static void AppendModelToReg(List<string> lines, InstalledModel model)
    {
        var tokenName = $"Sherpa-{model.Id}";
        var tokenPath = $@"HKEY_LOCAL_MACHINE\{SapiTokensRoot}\{tokenName}";

        // Main token key
        lines.Add($"[{tokenPath}]");
        lines.Add($"@=\"Sherpa {model.Id}\"");
        lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");

        // VoiceGardenConfig subkey
        lines.Add($"[{tokenPath}\\VoiceGardenConfig]");
        lines.Add("\"EngineType\"=\"Sherpa\"");
        lines.Add($"\"SherpaOnnxModelType\"=dword:{model.ModelType:X8}");
        lines.Add($"\"SherpaOnnxModelPath\"=\"{EscapeRegPath(model.ModelPath!)}\"");
        if (model.TokensPath != null)
            lines.Add($"\"SherpaOnnxTokens\"=\"{EscapeRegPath(model.TokensPath)}\"");
        if (model.DataDir != null)
            lines.Add($"\"SherpaOnnxDataDir\"=\"{EscapeRegPath(model.DataDir)}\"");
        if (model.VoicesPath != null)
            lines.Add($"\"SherpaOnnxVoices\"=\"{EscapeRegPath(model.VoicesPath)}\"");
        if (model.LexiconPath != null)
            lines.Add($"\"SherpaOnnxLexicon\"=\"{EscapeRegPath(model.LexiconPath)}\"");

        // Attributes subkey
        lines.Add($"[{tokenPath}\\Attributes]");
        lines.Add($"\"Name\"=\"{model.Id}\"");
        lines.Add($"\"Gender\"=\"{model.Gender}\"");
        lines.Add("\"Age\"=\"Adult\"");
        lines.Add("\"Language\"=\"409\"");
        lines.Add("\"Locale\"=\"en-US\"");
        lines.Add("\"Vendor\"=\"K2FSA\"");
        lines.Add("\"VoiceGardenType\"=\"Sherpa;Offline\"");
        if (!string.IsNullOrEmpty(model.Quality) && model.Quality != "unknown")
            lines.Add($"\"Quality\"=\"{model.Quality}\"");

        // Also register in Speech_OneCore for Chrome/Edge support
        var oneCorePath = $@"HKEY_LOCAL_MACHINE\{OneCoreTokensRoot}\{tokenName}";
        lines.Add($"[{oneCorePath}]");
        lines.Add($"@=\"Sherpa {model.Id}\"");
        lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");
        lines.Add($"[{oneCorePath}\\VoiceGardenConfig]");
        lines.Add("\"EngineType\"=\"Sherpa\"");
        lines.Add($"\"SherpaOnnxModelType\"=dword:{model.ModelType:X8}");
        lines.Add($"\"SherpaOnnxModelPath\"=\"{EscapeRegPath(model.ModelPath!)}\"");
        if (model.TokensPath != null)
            lines.Add($"\"SherpaOnnxTokens\"=\"{EscapeRegPath(model.TokensPath)}\"");
        if (model.DataDir != null)
            lines.Add($"\"SherpaOnnxDataDir\"=\"{EscapeRegPath(model.DataDir)}\"");
        if (model.VoicesPath != null)
            lines.Add($"\"SherpaOnnxVoices\"=\"{EscapeRegPath(model.VoicesPath)}\"");
        if (model.LexiconPath != null)
            lines.Add($"\"SherpaOnnxLexicon\"=\"{EscapeRegPath(model.LexiconPath)}\"");
        lines.Add($"[{oneCorePath}\\Attributes]");
        lines.Add($"\"Name\"=\"{model.Id}\"");
        lines.Add($"\"Gender\"=\"{model.Gender}\"");
        lines.Add("\"Age\"=\"Adult\"");
        lines.Add("\"Language\"=\"409\"");
        lines.Add("\"Locale\"=\"en-US\"");
        lines.Add("\"Vendor\"=\"K2FSA\"");
        if (!string.IsNullOrEmpty(model.Quality) && model.Quality != "unknown")
            lines.Add($"\"Quality\"=\"{model.Quality}\"");
        lines.Add("");
    }

    private static string EscapeRegPath(string path) => path.Replace("\\", "\\\\");

    /// <summary>
    /// Promote a single SherpaOnnx model to HKLM.
    /// </summary>
    public static void PromoteSherpaModel(InstalledModel model)
    {
        if (model.ModelPath == null) return;

        var tokenName = $"Sherpa-{model.Id}";
        var tokenPath = $@"{SapiTokensRoot}\{tokenName}";

        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(tokenPath, writable: true)
            ?? throw new InvalidOperationException("Cannot create HKLM token (admin required)");

        key.SetValue("", $"Sherpa {model.Id}", Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("CLSID", TtsEngineClsid, Microsoft.Win32.RegistryValueKind.String);

        using var config = key.CreateSubKey("VoiceGardenConfig", writable: true);
        config.SetValue("EngineType", "Sherpa", Microsoft.Win32.RegistryValueKind.String);
        config.SetValue("SherpaOnnxModelType", model.ModelType, Microsoft.Win32.RegistryValueKind.DWord);
        config.SetValue("SherpaOnnxModelPath", model.ModelPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.TokensPath != null)
            config.SetValue("SherpaOnnxTokens", model.TokensPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.DataDir != null)
            config.SetValue("SherpaOnnxDataDir", model.DataDir, Microsoft.Win32.RegistryValueKind.String);
        if (model.VoicesPath != null)
            config.SetValue("SherpaOnnxVoices", model.VoicesPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.LexiconPath != null)
            config.SetValue("SherpaOnnxLexicon", model.LexiconPath, Microsoft.Win32.RegistryValueKind.String);

        using var attrs = key.CreateSubKey("Attributes", writable: true);
        attrs.SetValue("Name", model.Id, Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Gender", model.Gender, Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Age", "Adult", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Language", "409", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Locale", "en-US", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Vendor", "K2FSA", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("VoiceGardenType", "Sherpa;Offline", Microsoft.Win32.RegistryValueKind.String);
        if (!string.IsNullOrEmpty(model.Quality) && model.Quality != "unknown")
            attrs.SetValue("Quality", model.Quality, Microsoft.Win32.RegistryValueKind.String);
        else
            attrs.DeleteValue("Quality", throwOnMissingValue: false);

        // Also register in Speech_OneCore for Chrome/Edge support
        var ocTokenPath = $@"{OneCoreTokensRoot}\{tokenName}";
        using var ocKey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(ocTokenPath, writable: true);
        if (ocKey != null)
        {
            ocKey.SetValue("", $"Sherpa {model.Id}", Microsoft.Win32.RegistryValueKind.String);
            ocKey.SetValue("CLSID", TtsEngineClsid, Microsoft.Win32.RegistryValueKind.String);

            using var ocConfig = ocKey.CreateSubKey("VoiceGardenConfig", writable: true);
            ocConfig.SetValue("EngineType", "Sherpa", Microsoft.Win32.RegistryValueKind.String);
            ocConfig.SetValue("SherpaOnnxModelType", model.ModelType, Microsoft.Win32.RegistryValueKind.DWord);
            ocConfig.SetValue("SherpaOnnxModelPath", model.ModelPath, Microsoft.Win32.RegistryValueKind.String);
            if (model.TokensPath != null)
                ocConfig.SetValue("SherpaOnnxTokens", model.TokensPath, Microsoft.Win32.RegistryValueKind.String);
            if (model.DataDir != null)
                ocConfig.SetValue("SherpaOnnxDataDir", model.DataDir, Microsoft.Win32.RegistryValueKind.String);
            if (model.VoicesPath != null)
                ocConfig.SetValue("SherpaOnnxVoices", model.VoicesPath, Microsoft.Win32.RegistryValueKind.String);
            if (model.LexiconPath != null)
                ocConfig.SetValue("SherpaOnnxLexicon", model.LexiconPath, Microsoft.Win32.RegistryValueKind.String);

            using var ocAttrs = ocKey.CreateSubKey("Attributes", writable: true);
            ocAttrs.SetValue("Name", model.Id, Microsoft.Win32.RegistryValueKind.String);
            ocAttrs.SetValue("Gender", model.Gender, Microsoft.Win32.RegistryValueKind.String);
            ocAttrs.SetValue("Age", "Adult", Microsoft.Win32.RegistryValueKind.String);
            ocAttrs.SetValue("Language", "409", Microsoft.Win32.RegistryValueKind.String);
            ocAttrs.SetValue("Locale", "en-US", Microsoft.Win32.RegistryValueKind.String);
            ocAttrs.SetValue("Vendor", "K2FSA", Microsoft.Win32.RegistryValueKind.String);
            if (!string.IsNullOrEmpty(model.Quality) && model.Quality != "unknown")
                ocAttrs.SetValue("Quality", model.Quality, Microsoft.Win32.RegistryValueKind.String);
            else
                ocAttrs.DeleteValue("Quality", throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Remove a SherpaOnnx voice from HKLM.
    /// </summary>
    public static void UnpromoteSherpaModel(string modelId)
    {
        var tokenName = $"Sherpa-{modelId}";
        try
        {
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                $@"{SapiTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                $@"{OneCoreTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
        }
        catch { }
    }

    public static HashSet<string> GetPromotedSherpaTokens()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SapiTokensRoot);
        if (key == null) return result;
        foreach (var name in key.GetSubKeyNames())
        {
            if (name.StartsWith("Sherpa-", StringComparison.OrdinalIgnoreCase))
                result.Add(name);
        }
        return result;
    }

    public static string GetModelsDir() => ModelsDir;
}
