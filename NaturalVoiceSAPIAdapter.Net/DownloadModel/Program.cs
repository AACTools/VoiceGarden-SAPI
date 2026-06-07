using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

if (args.Length == 0)
{
    Console.WriteLine("Usage: download-model <command> [args]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  list [filter]          List available models (optional filter)");
    Console.WriteLine("  download <model-id>    Download a model");
    Console.WriteLine("  path                   Show model directory path");
    return;
}

var modelDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".dotnet-tts-wrapper", "models");

if (args[0] == "path")
{
    Console.WriteLine(modelDir);
    return;
}

var catalog = LoadModelCatalog();

if (args[0] == "list")
{
    var filter = args.Length > 1 ? args[1].ToLowerInvariant() : "";
    foreach (var entry in catalog)
    {
        if (!string.IsNullOrEmpty(filter) && !entry.Key.Contains(filter)) continue;
        var info = entry.Value;
        Console.WriteLine($"  {entry.Key,-45} {info.ModelType,-8} {info.FileSizeMb,6}MB  {info.Name}");
    }
    return;
}

if (args[0] == "download")
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: download-model download <model-id>");
        return;
    }

    var modelId = args[1];
    if (!catalog.TryGetValue(modelId, out var modelInfo))
    {
        Console.WriteLine($"Model '{modelId}' not found. Use 'list' to see available models.");
        return;
    }

    var targetDir = Path.Combine(modelDir, modelId);
    if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir, "*.onnx").Length > 0)
    {
        Console.WriteLine($"Model already exists at {targetDir}");
        return;
    }

    if (string.IsNullOrEmpty(modelInfo.Url))
    {
        Console.WriteLine($"No download URL for model '{modelId}'");
        return;
    }

    Console.WriteLine($"Downloading {modelId} ({modelInfo.FileSizeMb}MB)...");
    Console.WriteLine($"  URL: {modelInfo.Url}");

    Directory.CreateDirectory(modelDir);
    var tempFile = Path.Combine(Path.GetTempPath(), $"{modelId}.tar.bz2");

    try
    {
        using var hc = new HttpClient();
        var response = await hc.GetAsync(modelInfo.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? (long)(modelInfo.FileSizeMb * 1024 * 1024);
        var downloaded = 0L;

        await using (var fs = File.Create(tempFile))
        await using (var stream = await response.Content.ReadAsStreamAsync())
        {
            var buf = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buf)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, read));
                downloaded += read;
                var pct = total > 0 ? (int)(downloaded * 100 / total) : 0;
                Console.Write($"\r  {pct,3}% ({downloaded / 1024 / 1024}MB / {total / 1024 / 1024}MB)");
            }
        }
        Console.WriteLine("\n  Download complete. Extracting...");

        ExtractTarBz2(tempFile, targetDir);
        Console.WriteLine($"  Extracted to {targetDir}");

        var onnxFiles = Directory.GetFiles(targetDir, "*.onnx", SearchOption.AllDirectories);
        Console.WriteLine($"  Found {onnxFiles.Length} .onnx file(s)");
    }
    finally
    {
        if (File.Exists(tempFile)) File.Delete(tempFile);
    }
    return;
}

Console.WriteLine($"Unknown command: {args[0]}");

Dictionary<string, ModelInfo> LoadModelCatalog()
{
    var asm = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "DotNetTtsWrapper.Core");

    if (asm == null)
    {
        try
        {
            asm = Assembly.Load("DotNetTtsWrapper.Core");
        }
        catch { }
    }

    if (asm == null)
    {
        Console.WriteLine("Error: DotNetTtsWrapper.Core assembly not found");
        return new Dictionary<string, ModelInfo>();
    }

    var resName = asm.GetManifestResourceNames()
        .FirstOrDefault(n => n.Contains("merged_models")) ?? "";

    if (string.IsNullOrEmpty(resName))
    {
        Console.WriteLine("Warning: merged_models.json not found in assembly resources");
        return new Dictionary<string, ModelInfo>();
    }

    using var stream = asm.GetManifestResourceStream(resName)!;
    var doc = JsonDocument.Parse(stream);
    var result = new Dictionary<string, ModelInfo>();

    foreach (var prop in doc.RootElement.EnumerateObject())
    {
        var el = prop.Value;
        result[prop.Name] = new ModelInfo
        {
            Id = prop.Name,
            ModelType = el.TryGetProperty("model_type", out var mt) ? mt.GetString() ?? "" : "",
            Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
            FileSizeMb = el.TryGetProperty("filesize_mb", out var fs) ? fs.GetDouble() : 0,
        };
    }
    return result;
}

void ExtractTarBz2(string archivePath, string targetDir)
{
    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = "tar",
        Arguments = $"-xjf \"{archivePath}\" -C \"{targetDir}\" --strip-components=1",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    });
    process!.WaitForExit(120000);
    if (process.ExitCode != 0)
    {
        var err = process.StandardError.ReadToEnd();
        throw new Exception($"tar extraction failed (exit {process.ExitCode}): {err}");
    }
}

record ModelInfo
{
    public string Id = "";
    public string ModelType = "";
    public string Name = "";
    public string Url = "";
    public double FileSizeMb;
}
