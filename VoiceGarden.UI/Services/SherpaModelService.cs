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
    }

    /// <summary>
    /// Load the model catalog from merged_models.json (embedded or sidecar).
    /// </summary>
    public static async Task<List<CatalogModel>> LoadCatalogAsync()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "merged_models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "merged_models.json"),
            Path.Combine(AppContext.BaseDirectory, "x64", "merged_models.json"),
            Path.Combine(AppContext.BaseDirectory, "x86", "merged_models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SherpaOnnxConfig", "merged_models.json"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);

                // merged_models.json is a dict keyed by model ID: { "id": { ... }, ... }
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
    /// Scan the local models directory for installed models.
    /// </summary>
    public static List<InstalledModel> ScanInstalledModels()
    {
        var result = new List<InstalledModel>();
        if (!Directory.Exists(ModelsDir)) return result;

        // Check which are already promoted to HKLM
        var promoted = GetPromotedSherpaTokens();

        foreach (var dir in Directory.GetDirectories(ModelsDir))
        {
            var modelId = Path.GetFileName(dir);

            // Auto-extract any orphaned .tar.bz2 left from a failed/aborted extraction
            TryExtractArchives(dir);

            var installed = new InstalledModel
            {
                Id = modelId,
                Directory = dir,
                IsPromoted = promoted.Contains($"Sherpa-{modelId}"),
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
    /// extract it with 7-Zip. Self-heals downloads that completed but never extracted.
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

        var sevenZip = @"C:\Program Files\7-Zip\7z.exe";
        if (!File.Exists(sevenZip)) return;

        // Stage 1: tar.bz2 -> tar
        var bz2 = Directory.GetFiles(dir, "*.tar.bz2", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (bz2 != null)
        {
            var tar = bz2.Replace(".tar.bz2", ".tar");
            if (!File.Exists(tar))
            {
                RunSevenZip(sevenZip, $"x \"{bz2}\" -o\"{dir}\" -y");
            }
            // Stage 2: tar -> contents
            if (File.Exists(tar))
            {
                RunSevenZip(sevenZip, $"x \"{tar}\" -o\"{dir}\" -y");
                TryDelete(tar);
            }
            TryDelete(bz2);
        }
        else
        {
            // Lone .tar
            var tar = Directory.GetFiles(dir, "*.tar", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (tar != null)
            {
                RunSevenZip(sevenZip, $"x \"{tar}\" -o\"{dir}\" -y");
                TryDelete(tar);
            }
        }
    }

    private static void RunSevenZip(string exe, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(120000); // 2-min cap per stage
        }
        catch { /* best-effort */ }
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

        // Extract the archive
        progress?.Report((100, "Extracting..."));
        var sevenZip = @"C:\Program Files\7-Zip\7z.exe";
        if (!File.Exists(sevenZip))
        {
            throw new PlatformNotSupportedException(
                "7-Zip is required for model extraction. Install 7-Zip from https://7-zip.org then click Rescan.");
        }

        if (lastSegment.EndsWith(".tar.bz2"))
        {
            var tarFile = destFile.Replace(".tar.bz2", ".tar");
            RunSevenZip(sevenZip, $"x \"{destFile}\" -o\"{destDir}\" -y");
            if (File.Exists(tarFile))
                RunSevenZip(sevenZip, $"x \"{tarFile}\" -o\"{destDir}\" -y");
            TryDelete(tarFile);
        }
        else
        {
            RunSevenZip(sevenZip, $"x \"{destFile}\" -o\"{destDir}\" -y");
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
        lines.Add("\"Gender\"=\"Neutral\"");
        lines.Add("\"Age\"=\"Adult\"");
        lines.Add("\"Language\"=\"409\"");
        lines.Add("\"Locale\"=\"en-US\"");
        lines.Add("\"Vendor\"=\"K2FSA\"");
        lines.Add("\"VoiceGardenType\"=\"Sherpa;Offline\"");
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
        attrs.SetValue("Gender", "Neutral", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Age", "Adult", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Language", "409", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Locale", "en-US", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Vendor", "K2FSA", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("VoiceGardenType", "Sherpa;Offline", Microsoft.Win32.RegistryValueKind.String);
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
