// Unit tests for the piper sidecar generator (issue #15 stage 5, SPD
// installer-parity port): generation from sherpa layout, stale-sidecar
// re-run, phoneme_id_map casefold, and language_code derivation.

using System.Text.Json;
using VoiceGarden.UI.Services;
using Xunit;

namespace VoiceGarden.UI.Tests;

public sealed class PiperSidecarGeneratorTests : IDisposable
{
    private readonly string _dir;

    public PiperSidecarGeneratorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vg-sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void WriteSherpaLayout(string tokens)
    {
        File.WriteAllText(Path.Combine(_dir, "model.onnx"), "fake-onnx");
        File.WriteAllText(Path.Combine(_dir, "tokens.txt"), tokens);
    }

    private JsonDocument ReadSidecar(string path)
    {
        Assert.True(File.Exists(path), $"sidecar not written: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void GeneratesSidecarForSherpaLayout()
    {
        WriteSherpaLayout("a 1\nb 2 3\n");

        var written = PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: 16000);

        Assert.NotNull(written);
        using var doc = ReadSidecar(written!);
        var root = doc.RootElement;

        Assert.Equal(16000, root.GetProperty("audio").GetProperty("sample_rate").GetInt32());
        Assert.Equal(1, root.GetProperty("num_speakers").GetInt32());

        var map = root.GetProperty("phoneme_id_map");
        Assert.Equal(new long[] { 1 }, map.GetProperty("a").EnumerateArray().Select(e => e.GetInt64()).ToArray());
        Assert.Equal(new long[] { 2, 3 }, map.GetProperty("b").EnumerateArray().Select(e => e.GetInt64()).ToArray());
    }

    [Fact]
    public void CasefoldsPhonemeMapKeys()
    {
        // sherpa token tables ship single-case symbols; the G2P chain looks
        // up phonemes case-insensitively (SPD parity fix).
        WriteSherpaLayout("ɑ 1\nˈ 5\n");

        var written = PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: null);

        using var doc = ReadSidecar(written!);
        var map = doc.RootElement.GetProperty("phoneme_id_map");
        Assert.True(map.TryGetProperty("ɑ", out _));
        // Folded duplicates are NOT invented for symbols that differ only in
        // case from an existing key, but invariant folding of a symbol with
        // no case ("ɑ" folds to itself) must not corrupt the map.
        Assert.Equal(1, map.GetProperty("ɑ").EnumerateArray().First().GetInt64());
    }

    [Fact]
    public void CasefoldAddsMissingVariant()
    {
        // "AA" exists, "aa" does not → folded variant is added with same ids.
        WriteSherpaLayout("AA 7\n");

        var written = PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: null);

        using var doc = ReadSidecar(written!);
        var map = doc.RootElement.GetProperty("phoneme_id_map");
        Assert.True(map.TryGetProperty("aa", out var folded));
        Assert.Equal(7, folded.EnumerateArray().First().GetInt64());
        Assert.True(map.TryGetProperty("AA", out var original));
        Assert.Equal(7, original.EnumerateArray().First().GetInt64());
    }

    [Theory]
    [InlineData("piper-en_US-amy-low", "en-US")]
    [InlineData("piper-fa_IR-amir-medium", "fa-IR")]
    [InlineData("mms_eng", "en")]
    [InlineData("mms_fas", "fa")]
    [InlineData("coqui-en-ljspeech", "")]       // unknown family — no language
    [InlineData("kokoro-en-v0_19", "")]         // kokoro → no sidecar language
    public void DerivesLanguageCode(string modelId, string expected)
    {
        Assert.Equal(expected, PiperSidecarGenerator.DeriveLanguageCode(modelId));
    }

    [Fact]
    public void LanguageCodeLandsInSidecar()
    {
        WriteSherpaLayout("a 1\n");

        var written = PiperSidecarGenerator.EnsureSidecar(_dir, "piper-fa_IR-amir-medium", sampleRate: null);

        using var doc = ReadSidecar(written!);
        Assert.Equal("fa-IR", doc.RootElement.GetProperty("language").GetProperty("code").GetString());
    }

    [Fact]
    public void SkipsKokoroVoices()
    {
        WriteSherpaLayout("a 1\n");
        File.WriteAllText(Path.Combine(_dir, "voices.bin"), "fake");

        var written = PiperSidecarGenerator.EnsureSidecar(_dir, "kokoro-en-v0_19", sampleRate: null);

        Assert.Null(written);
    }

    [Fact]
    public void RegeneratesOurStaleSidecar_NeverShippedOnes()
    {
        WriteSherpaLayout("a 1\n");
        var sidecar = Path.Combine(_dir, "model.onnx.json");

        // First run writes it (marked as ours).
        Assert.NotNull(PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: 16000));

        // Fresh: no rewrite.
        File.SetLastWriteTimeUtc(sidecar, DateTime.UtcNow.AddMinutes(1));
        Assert.Null(PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: 16000));

        // Model updated in place (tokens.txt newer) → our sidecar is
        // re-generated with the new map (SPD "generate_sidecar() re-run" rule).
        File.WriteAllText(Path.Combine(_dir, "tokens.txt"), "a 1\nz 9\n");
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "tokens.txt"), DateTime.UtcNow.AddMinutes(2));
        var rewritten = PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: 16000);
        Assert.NotNull(rewritten);
        using var doc = ReadSidecar(rewritten!);
        Assert.True(doc.RootElement.GetProperty("phoneme_id_map").TryGetProperty("z", out _));

        // A SHIPPED sidecar (piper release / floravox-patched, no marker) is
        // never overwritten — regenerating would drop its espeak/inference
        // config even when tokens.txt is newer.
        File.Delete(sidecar);
        File.WriteAllText(sidecar,
            """{ "espeak": { "voice": "en-us" }, "inference": { "noise_scale": 0.667 } }""");
        File.SetLastWriteTimeUtc(sidecar, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "tokens.txt"), DateTime.UtcNow);
        Assert.Null(PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: 16000));
        Assert.Contains("noise_scale", File.ReadAllText(sidecar));
    }

    [Fact]
    public void NoSidecarWithoutTokensFile()
    {
        File.WriteAllText(Path.Combine(_dir, "model.onnx"), "fake-onnx");

        Assert.Null(PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: null));
    }

    [Fact]
    public void TokensParserSkipsCountHeaderAndMergesIds()
    {
        var tokens = Path.Combine(_dir, "tokens.txt");
        File.WriteAllText(tokens, "3\na 1\nb 2\na 3\n");

        var map = PiperSidecarGenerator.ParseTokensFile(tokens);

        Assert.False(map.ContainsKey("3")); // header skipped
        Assert.Equal(new[] { 1L, 3L }, map["a"]);
        Assert.Equal(new[] { 2L }, map["b"]);
    }

    [Fact]
    public void ReadsSampleRateFromMmsConfigJson()
    {
        WriteSherpaLayout("a 1\n");
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            """{ "data": { "sampling_rate": 16000, "hop_length": 256 } }""");

        var written = PiperSidecarGenerator.EnsureSidecar(_dir, "mms_eng", sampleRate: null);

        using var doc = ReadSidecar(written!);
        Assert.Equal(16000, doc.RootElement.GetProperty("audio").GetProperty("sample_rate").GetInt32());
    }
}
