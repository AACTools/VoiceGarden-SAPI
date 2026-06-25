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
        "NaturalVoiceSAPIAdapter", "models");

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

        progress?.Report((0, $"Downloading {fileName}..."));

        using var http = new HttpClient();
        using var response = await http.GetAsync(model.Url);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(destFile);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;
            if (totalBytes > 0)
            {
                var pct = (int)(bytesRead * 100 / totalBytes);
                progress?.Report((pct, $"Downloading... {pct}%"));
            }
        }

        // Extract if tar.bz2
        if (fileName.EndsWith(".tar.bz2"))
        {
            progress?.Report((100, "Extracting..."));
            ExtractTarBz2(destFile, destDir);
            File.Delete(destFile);
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

        using var config = key.CreateSubKey("NaturalVoiceConfig", writable: true);
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
        attrs.SetValue("NaturalVoiceType", "Sherpa;Offline", Microsoft.Win32.RegistryValueKind.String);
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

    /// <summary>
    /// Minimal tar.bz2 extraction using 7-Zip if available, otherwise falls back to .NET tar.
    /// </summary>
    private static void ExtractTarBz2(string archivePath, string destDir)
    {
        // Try 7z
        var sevenZip = @"C:\Program Files\7-Zip\7z.exe";
        if (File.Exists(sevenZip))
        {
            // Extract bz2 → tar
            var tarFile = archivePath.Replace(".tar.bz2", ".tar");
            var psi1 = new System.Diagnostics.ProcessStartInfo(sevenZip, $"x \"{archivePath}\" -o\"{Path.GetDirectoryName(archivePath)}\" -y")
            { WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, CreateNoWindow = true };
            var p1 = System.Diagnostics.Process.Start(psi1);
            p1?.WaitForExit();

            // Extract tar
            if (File.Exists(tarFile))
            {
                var psi2 = new System.Diagnostics.ProcessStartInfo(sevenZip, $"x \"{tarFile}\" -o\"{destDir}\" -y")
                { WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, CreateNoWindow = true };
                var p2 = System.Diagnostics.Process.Start(psi2);
                p2?.WaitForExit();
                File.Delete(tarFile);
            }
            return;
        }

        // Fallback: .NET 8 tar + bz2 decompression
        throw new PlatformNotSupportedException("7-Zip required for model extraction. Install 7-Zip or extract manually.");
    }
}
