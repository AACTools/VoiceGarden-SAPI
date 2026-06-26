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
        public bool IsPromoted { get; set; }
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
            var onnxFiles = Directory.GetFiles(dir, "*.onnx", SearchOption.AllDirectories);
            if (onnxFiles.Length > 0)
            {
                installed.ModelPath = onnxFiles[0];
                var modelDir = Path.GetDirectoryName(installed.ModelPath)!;

                var tokensPath = Path.Combine(modelDir, "tokens.txt");
                if (File.Exists(tokensPath))
                    installed.TokensPath = tokensPath;

                var dataDir = Path.Combine(modelDir, "espeak-ng-data");
                if (Directory.Exists(dataDir))
                    installed.DataDir = dataDir;
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

    /// <summary>
    /// Download a model from the catalog URL.
    /// </summary>
    public static async Task DownloadModelAsync(CatalogModel model, IProgress<(int percent, string status)>? progress = null)
    {
        if (string.IsNullOrEmpty(model.Url))
            throw new InvalidOperationException($"Model {model.Id} has no download URL");

        var destDir = Path.Combine(ModelsDir, model.Id);
        Directory.CreateDirectory(destDir);

        var fileName = model.Url.Split('/').Last();
        var destFile = Path.Combine(destDir, fileName);

        progress?.Report((0, $"Connecting to {fileName}..."));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var totalMb = totalBytes > 0 ? totalBytes / (1024.0 * 1024.0) : 0;
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
                // Throttle progress reports to 4/sec to avoid flooding UI thread
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
                // Unknown size - report bytes downloaded
                var doneMb = bytesRead / (1024.0 * 1024.0);
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 500)
                {
                    lastReport = now;
                    progress?.Report((0, $"{doneMb:F0}MB downloaded"));
                }
            }
        }

        // Extract if tar.bz2 (the rescan path will also self-heal if this fails)
        if (fileName.EndsWith(".tar.bz2") || fileName.EndsWith(".tar"))
        {
            progress?.Report((100, "Extracting..."));
            var sevenZip = @"C:\Program Files\7-Zip\7z.exe";
            if (File.Exists(sevenZip))
            {
                if (fileName.EndsWith(".tar.bz2"))
                {
                    var tarFile = destFile.Replace(".tar.bz2", ".tar");
                    RunSevenZip(sevenZip, $"x \"{destFile}\" -o\"{destDir}\" -y");
                    if (File.Exists(tarFile))
                        RunSevenZip(sevenZip, $"x \"{tarFile}\" -o\"{destDir}\" -y");
                }
                else
                {
                    RunSevenZip(sevenZip, $"x \"{destFile}\" -o\"{destDir}\" -y");
                }
                TryDelete(destFile);
            }
            else
            {
                throw new PlatformNotSupportedException(
                    "7-Zip is required for model extraction. Install 7-Zip from https://7-zip.org then click Rescan.");
            }
        }

        progress?.Report((100, "Done"));
    }

    /// <summary>
    /// Promote all downloaded models to HKLM as SAPI tokens.
    /// </summary>
    public static (int promoted, int failed) PromoteAll(bool compatEnUs = false)
    {
        var models = ScanInstalledModels();
        int promoted = 0, failed = 0;

        foreach (var model in models.Where(m => m.ModelPath != null))
        {
            try
            {
                PromoteSherpaModel(model);
                promoted++;
            }
            catch
            {
                failed++;
            }
        }

        return (promoted, failed);
    }

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
        config.SetValue("SherpaOnnxModelType", 0, Microsoft.Win32.RegistryValueKind.DWord); // VITS
        config.SetValue("SherpaOnnxModelPath", model.ModelPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.TokensPath != null)
            config.SetValue("SherpaOnnxTokens", model.TokensPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.DataDir != null)
            config.SetValue("SherpaOnnxDataDir", model.DataDir, Microsoft.Win32.RegistryValueKind.String);

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
