using Microsoft.Win32;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Local GGUF-based offline engines (rust-tts-wrapper 1.6.5+): qwen3-local
/// (Qwen3-TTS via qwen3-tts.cpp) and omnivoice-local (voice cloning, 600+
/// languages). Downloads the GGUFs, tracks install state, and promotes
/// SAPI voice tokens (SAPI issue #19).
///
/// Model sources (HuggingFace, direct GGUF downloads):
///  - qwen3-local:   willwade/qwen3-tts-0.6b-gguf (converted with predict-woo's
///    pipeline — qwen3-tts.cpp's loader requires these exact file names).
///  - omnivoice-local: Serveurperso/OmniVoice-GGUF (canonical source for the
///    omnivoice.cpp engine the wrapper bundles).
/// </summary>
public static class LocalEngineService
{
    private const string TtsEngineClsid = "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}";
    private static readonly string s_tokensRoot = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";
    private static readonly string s_oneCoreRoot = @"SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens";

    public sealed record EngineFile(string FileName, string? Url, long ApproxBytes);

    public sealed record EngineVoice(string Id, string Bcp47, string Display);

    public sealed record LocalEngine(
        string EngineId,
        string DisplayName,
        string Vendor,
        string[] FileNames,
        string DownloadBaseUrl,
        string? UrlOverrideEnv,
        EngineVoice[] Voices)
    {
        /// <summary>Downloads are possible (direct URLs or override env set).</summary>
        public bool Downloadable => !string.IsNullOrEmpty(DownloadBaseUrl) ||
            (UrlOverrideEnv != null && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(UrlOverrideEnv)));

        public string BaseUrl => !string.IsNullOrEmpty(DownloadBaseUrl)
            ? DownloadBaseUrl
            : (Environment.GetEnvironmentVariable(UrlOverrideEnv ?? "") ?? "").TrimEnd('/');

        public string InstallDir => Path.Combine(EnginesRoot, EngineId);

        /// <summary>Rough download size across all files (UI display only).</summary>
        public string ApproxSizeText => EngineId == "omnivoice-local" ? "≈1.3 GB" : "≈1.6 GB";

        public (string url, string fileName, long bytes)[] FileDownloads() =>
            FileNames.Select(f => ($"{BaseUrl}/{f}", f, ApproxFileSizeBytes)).ToArray();

        private long ApproxFileSizeBytes => EngineId == "omnivoice-local" ? 1_300_000_000 : 1_200_000_000;
    }

    public static string EnginesRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceGardenSAPIAdapter", "engines");

    // qwen3 voice ids mirror rust-tts-wrapper's supported_languages() table.
    private static readonly EngineVoice[] Qwen3Voices =
    {
        new("default", "en", "Default timbre"),
        new("en", "en", "English"),
        new("zh", "zh-CN", "Chinese (Mandarin)"),
        new("ja", "ja", "Japanese"),
        new("ko", "ko", "Korean"),
        new("de", "de", "German"),
        new("fr", "fr", "French"),
        new("ru", "ru", "Russian"),
        new("es", "es", "Spanish"),
        new("it", "it", "Italian"),
        new("pt", "pt", "Portuguese"),
    };

    private static readonly EngineVoice[] OmniVoiceVoices =
    {
        new("omnivoice-local", "en", "OmniVoice local (600+ languages)"),
    };

    public static readonly LocalEngine[] Catalog =
    {
        new(
            EngineId: "omnivoice-local",
            DisplayName: "OmniVoice local",
            Vendor: "OmniVoice",
            FileNames: new[] { "omnivoice-base-Q8_0.gguf", "omnivoice-tokenizer-F32.gguf" },
            DownloadBaseUrl: "https://huggingface.co/Serveurperso/OmniVoice-GGUF/resolve/main",
            UrlOverrideEnv: null,
            Voices: OmniVoiceVoices),
        new(
            EngineId: "qwen3-local",
            DisplayName: "Qwen3-TTS local",
            Vendor: "Qwen",
            // The engine loader prefers q8_0 over f16 when present — ship
            // the quantized talker (f16 stays available for manual use).
            FileNames: new[] { "qwen3-tts-0.6b-q8_0.gguf", "qwen3-tts-tokenizer-f16.gguf" },
            DownloadBaseUrl: "https://huggingface.co/willwade/qwen3-tts-0.6b-gguf/resolve/main",
            UrlOverrideEnv: null,
            Voices: Qwen3Voices),
    };

    public static LocalEngine? Find(string engineId) =>
        Catalog.FirstOrDefault(e => e.EngineId.Equals(engineId, StringComparison.OrdinalIgnoreCase));

    /// <summary>All engine files exist on disk (engine usable).</summary>
    public static bool IsInstalled(LocalEngine engine) =>
        engine.FileNames.All(f => File.Exists(Path.Combine(engine.InstallDir, f)));

    /// <summary>Download all files of an engine (staged .part, then rename).</summary>
    public static async Task DownloadAsync(LocalEngine engine,
        IProgress<(int percent, string status)>? progress = null,
        HttpClient? http = null)
    {
        if (!engine.Downloadable)
        {
            throw new InvalidOperationException(
                $"{engine.EngineId}: no download source. Pre-converted GGUFs are not published " +
                "upstream yet — host qwen3-tts-0.6b-f16.gguf + qwen3-tts-tokenizer-f16.gguf and set " +
                $"{engine.UrlOverrideEnv} to their HTTPS base URL (see SAPI issue #19).");
        }

        http ??= new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("VoiceGarden-SAPI");
        Directory.CreateDirectory(engine.InstallDir);

        var downloads = engine.FileDownloads();
        for (var i = 0; i < downloads.Length; i++)
        {
            var (url, fileName, total) = downloads[i];
            var dest = Path.Combine(engine.InstallDir, fileName);
            var part = dest + ".part";
            if (File.Exists(dest)) continue; // already downloaded

            progress?.Report((100 * i / downloads.Length, $"Downloading {fileName}..."));
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var reportedTotal = resp.Content.Headers.ContentLength ?? total;

            {
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(part);
                var buffer = new byte[1 << 16];
                long copied = 0;
                int read;
                while ((read = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read));
                    copied += read;
                    if (reportedTotal > 0 && i == downloads.Length - 1)
                    {
                        var filePct = (int)(100 * copied / reportedTotal);
                        progress?.Report((Math.Min(99, 100 * i / downloads.Length + filePct / downloads.Length),
                            $"Downloading {fileName}... {copied / (1024 * 1024)} MB"));
                    }
                }
            }
            File.Move(part, dest, overwrite: true);
        }
        progress?.Report((100, "Done"));
    }

    /// <summary>Pure mapping used when writing a token — unit-testable.</summary>
    public static (string friendlyName, Dictionary<string, string> config, Dictionary<string, string> attributes)
        BuildTokenValues(LocalEngine engine, EngineVoice voice, string installDir)
    {
        var config = new Dictionary<string, string>
        {
            ["EngineType"] = engine.EngineId,
            ["Voice"] = voice.Id,
        };

        if (engine.EngineId == "qwen3-local")
        {
            config["Qwen3ModelsDir"] = installDir;
        }
        else if (engine.EngineId == "omnivoice-local")
        {
            config["OmniVoiceModelPath"] = Path.Combine(installDir, "omnivoice-base-Q8_0.gguf");
            config["OmniVoiceCodecPath"] = Path.Combine(installDir, "omnivoice-tokenizer-F32.gguf");
        }

        var locale = voice.Bcp47.Contains('-') ? voice.Bcp47 : DefaultLocaleFor(voice.Bcp47);
        var attributes = new Dictionary<string, string>
        {
            ["Name"] = $"{engine.DisplayName} ({voice.Display})",
            ["Gender"] = "Neutral",
            ["Age"] = "Adult",
            ["Locale"] = locale,
            ["Vendor"] = engine.Vendor,
            ["VoiceGardenType"] = "Local;Offline",
        };

        var friendly = $"{engine.DisplayName} ({voice.Display})";
        return (friendly, config, attributes);
    }

    private static string DefaultLocaleFor(string bcp47) => bcp47 switch
    {
        "en" => "en-US",
        "zh" => "zh-CN",
        "ja" => "ja-JP",
        "ko" => "ko-KR",
        "de" => "de-DE",
        "fr" => "fr-FR",
        "ru" => "ru-RU",
        "es" => "es-ES",
        "it" => "it-IT",
        "pt" => "pt-BR",
        _ => bcp47,
    };

    /// <summary>
    /// Promote a voice of an installed engine to HKLM (both token roots).
    /// Returns false on access denied (elevation needed) or missing files.
    /// </summary>
    public static bool Promote(LocalEngine engine, EngineVoice voice)
    {
        if (!IsInstalled(engine)) return false;
        var tokenName = $"Local-{engine.EngineId}-{voice.Id}";
        var (friendly, config, attributes) = BuildTokenValues(engine, voice, engine.InstallDir);

        try
        {
            WriteToken(s_tokensRoot, tokenName, friendly, config, attributes);
            WriteToken(s_oneCoreRoot, tokenName, friendly, config, attributes);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch { return false; }
    }

    /// <summary>
    /// Promote with UAC fallback: direct HKLM write when elevated, else a
    /// generated .reg imported via reg.exe (mirrors the cloud/sherpa paths).
    /// 0 = ok, -2 = UAC cancelled, other = failure.
    /// </summary>
    public static int PromoteElevated(LocalEngine engine, EngineVoice voice)
    {
        if (Promote(engine, voice)) return 0;
        if (!IsInstalled(engine)) return -1;

        var (friendly, config, attributes) = BuildTokenValues(engine, voice, engine.InstallDir);
        var tokenName = $"Local-{engine.EngineId}-{voice.Id}";
        var langId = SapiLanguage.TryResolve(attributes.GetValueOrDefault("Locale", "en-US"), out _, out var id) ? id : "409";

        var lines = new List<string> { "Windows Registry Editor Version 5.00", "" };
        foreach (var root in new[] { s_tokensRoot, s_oneCoreRoot })
        {
            lines.Add($"[HKEY_LOCAL_MACHINE\\{root}\\{tokenName}]");
            lines.Add($"@=\"{RegEscape(friendly)}\"");
            lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");
            lines.Add($"[HKEY_LOCAL_MACHINE\\{root}\\{tokenName}\\VoiceGardenConfig]");
            foreach (var (key, value) in config)
                lines.Add($"\"{key}\"=\"{RegEscape(value)}\"");
            lines.Add($"[HKEY_LOCAL_MACHINE\\{root}\\{tokenName}\\Attributes]");
            foreach (var (key, value) in attributes)
                lines.Add($"\"{key}\"=\"{RegEscape(value)}\"");
            lines.Add($"\"Language\"=\"{langId}\"");
            lines.Add("");
        }

        var regDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VoiceGardenSAPIAdapter");
        Directory.CreateDirectory(regDir);
        var regPath = Path.Combine(regDir, "promote_local_engine.reg");
        File.WriteAllLines(regPath, lines);

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
            p?.WaitForExit(15000);
            return p?.ExitCode ?? -1;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return -2; // UAC cancelled
        }
        finally
        {
            try { File.Delete(regPath); } catch { }
        }
    }

    private static string RegEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Promote every voice of every installed engine. (promote-all)</summary>
    public static (int promoted, int failed) PromoteAll()
    {
        int promoted = 0, failed = 0;
        foreach (var engine in Catalog.Where(IsInstalled))
        {
            foreach (var voice in engine.Voices)
            {
                if (Promote(engine, voice)) promoted++;
                else failed++;
            }
        }
        return (promoted, failed);
    }

    private static void WriteToken(string root, string tokenName, string friendlyName,
        Dictionary<string, string> config, Dictionary<string, string> attributes)
    {
        using var token = Registry.LocalMachine.CreateSubKey($@"{root}\{tokenName}", writable: true)
            ?? throw new InvalidOperationException("Cannot create HKLM token");
        token.SetValue("", friendlyName, RegistryValueKind.String);
        token.SetValue("CLSID", TtsEngineClsid, RegistryValueKind.String);

        using var cfg = token.CreateSubKey("VoiceGardenConfig");
        foreach (var (key, value) in config) cfg.SetValue(key, value, RegistryValueKind.String);

        using var attrs = token.CreateSubKey("Attributes");
        foreach (var (key, value) in attributes) attrs.SetValue(key, value, RegistryValueKind.String);
        // SAPI Language attribute = hex lang id; resolve via the shared helper
        if (SapiLanguage.TryResolve(attributes.GetValueOrDefault("Locale", "en-US"), out _, out var langId))
            attrs.SetValue("Language", langId, RegistryValueKind.String);
    }
}
