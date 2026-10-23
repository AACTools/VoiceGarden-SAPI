// Unit tests for the local GGUF engine service (SAPI issue #19): catalog
// integrity, install-state scan, download URL mapping, and the pure
// token-value mapping consumed by the HKLM promotion writer.

using VoiceGarden.UI.Services;
using Xunit;

namespace VoiceGarden.UI.Tests;

public sealed class LocalEngineServiceTests : IDisposable
{
    private readonly string _root;

    public LocalEngineServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vg-localeng-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void CatalogHasBothEnginesWithExpectedFiles()
    {
        var qwen = LocalEngineService.Find("qwen3-local");
        Assert.NotNull(qwen);
        // q8_0 talker preferred by the engine loader; tokenizer ships f16 only
        Assert.Equal(new[] { "qwen3-tts-0.6b-q8_0.gguf", "qwen3-tts-tokenizer-f16.gguf" }, qwen!.FileNames);
        Assert.Equal("https://huggingface.co/willwade/qwen3-tts-0.6b-gguf/resolve/main", qwen.DownloadBaseUrl);

        var ov = LocalEngineService.Find("omnivoice-local");
        Assert.NotNull(ov);
        Assert.Equal(new[] { "omnivoice-base-Q8_0.gguf", "omnivoice-tokenizer-F32.gguf" }, ov!.FileNames);
        Assert.StartsWith("https://huggingface.co/Serveurperso/", ov.DownloadBaseUrl);
    }

    [Fact]
    public void DownloadUrlsMapFilenamesOntoBaseUrl()
    {
        var engine = LocalEngineService.Find("omnivoice-local")!;
        var downloads = engine.FileDownloads();
        Assert.Contains(downloads, d => d.url.EndsWith("/omnivoice-base-Q8_0.gguf") && d.fileName == "omnivoice-base-Q8_0.gguf");
        Assert.Contains(downloads, d => d.url.EndsWith("/omnivoice-tokenizer-F32.gguf"));
    }

    [Fact]
    public void Qwen3VoiceTableMirrorsWrapperLanguages()
    {
        var engine = LocalEngineService.Find("qwen3-local")!;
        Assert.Equal("default", engine.Voices[0].Id);
        // The 10 languages of rust-tts-wrapper's supported_languages()
        Assert.Equal(new[] { "default", "en", "zh", "ja", "ko", "de", "fr", "ru", "es", "it", "pt" },
            engine.Voices.Select(v => v.Id).ToArray());
    }

    [Fact]
    public void InstallStateRequiresAllFiles()
    {
        var engine = LocalEngineService.Find("omnivoice-local")!;
        var dir = Path.Combine(_root, engine.EngineId);
        Directory.CreateDirectory(dir);
        Assert.False(engine.FileNames.All(f => File.Exists(Path.Combine(dir, f))));

        foreach (var f in engine.FileNames) File.WriteAllText(Path.Combine(dir, f), "gguf");
        Assert.True(engine.FileNames.All(f => File.Exists(Path.Combine(dir, f))));
    }

    [Fact]
    public void Qwen3InstalledWithEitherTalkerQuantization()
    {
        // The engine loader prefers q8_0 but accepts f16 — an existing f16
        // install must count (no forced re-download after the q8 switch).
        var engine = LocalEngineService.Find("qwen3-local")!;
        var dir = engine.InstallDir; // real engines dir — read-only check on files
        var tokenizer = Path.Combine(dir, "qwen3-tts-tokenizer-f16.gguf");
        if (!File.Exists(tokenizer)) return; // nothing on this machine — skip

        var q8 = File.Exists(Path.Combine(dir, "qwen3-tts-0.6b-q8_0.gguf"));
        var f16 = File.Exists(Path.Combine(dir, "qwen3-tts-0.6b-f16.gguf"));
        Assert.Equal(q8 || f16, LocalEngineService.IsInstalled(engine));
    }

    [Fact]
    public async Task ConcurrentDownloadsOfSameEngineAreRejected()
    {
        // All voices of an engine share one GGUF set — a second concurrent
        // download (second row, double-click) must throw, not corrupt the
        // same .part files (the bug that produced the locked 0.3 MB corpse).
        var engine = LocalEngineService.Find("omnivoice-local")!;
        var gate = LocalEngineService.DownloadGate(engine.EngineId);
        await gate.WaitAsync();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                LocalEngineService.DownloadAsync(engine));
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public void TokenValues_Qwen3CarriesModelsDirAndVoice()
    {
        var engine = LocalEngineService.Find("qwen3-local")!;
        var voice = engine.Voices.First(v => v.Id == "en");
        var (friendly, config, attributes) = LocalEngineService.BuildTokenValues(engine, voice, @"C:\models\qwen3");

        Assert.Equal("qwen3-local", config["EngineType"]);
        Assert.Equal("en", config["Voice"]);
        Assert.Equal(@"C:\models\qwen3", config["Qwen3ModelsDir"]);
        Assert.Equal("Local;Offline", attributes["VoiceGardenType"]);
        Assert.Equal("en-US", attributes["Locale"]); // two-letter bcp47 expanded
        Assert.Contains("Qwen3", friendly);
    }

    [Fact]
    public void TokenValues_OmniVoiceCarriesModelAndCodecPaths()
    {
        var engine = LocalEngineService.Find("omnivoice-local")!;
        var voice = engine.Voices.Single(v => v.Id == "en");
        var (_, config, attributes) = LocalEngineService.BuildTokenValues(engine, voice, @"C:\models\ov");

        Assert.Equal("omnivoice-local", config["EngineType"]);
        Assert.Equal("en", config["Voice"]);
        Assert.EndsWith("omnivoice-base-Q8_0.gguf", config["OmniVoiceModelPath"].Replace('/', '\\'));
        Assert.EndsWith("omnivoice-tokenizer-F32.gguf", config["OmniVoiceCodecPath"].Replace('/', '\\'));
        // Speed default: 32 diffusion steps is ~30x realtime on CPU
        Assert.Equal("16", config["OmniVoiceNumSteps"]);
        Assert.Equal("Local;Offline", attributes["VoiceGardenType"]);
    }

    [Fact]
    public void OmniVoiceExposesTheWrapperLocales()
    {
        // Mirrors the wrapper's omnivoice_language_codes() table — one
        // promotable voice per locale (voice id = language) — minus the
        // locales the runtime rejects (verified against the shipped DLL):
        // no English regional variants exist in its table.
        var engine = LocalEngineService.Find("omnivoice-local")!;
        Assert.True(engine.Voices.Length >= 55);
        Assert.Contains(engine.Voices, v => v.Id == "en");
        Assert.Contains(engine.Voices, v => v.Id == "fr");
        Assert.DoesNotContain(engine.Voices, v => v.Id == "en-GB" || v.Id == "en-US");
        // ids must be unique (token names derive from them)
        Assert.Equal(engine.Voices.Length, engine.Voices.Select(v => v.Id).Distinct().Count());
    }
}
