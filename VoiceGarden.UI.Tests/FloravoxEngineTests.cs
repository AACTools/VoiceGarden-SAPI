// Integration tests for the floravox engine through the shipped NuGet DLL
// (issue #15 stage 4). These exercise the exact native binary the SAPI
// adapter loads: RustTtsWrapper.Bindings 0.5.3 with sherpaonnx +
// floravox-lexicons on win-x64.
//
// Boundary/mark timings require a real piper voice. They are skipped when
// no voice is found under %LOCALAPPDATA%\VoiceGardenSAPIAdapter\models so CI
// without models still runs the offline checks.

using RustTtsWrapper;
using Xunit;

namespace VoiceGarden.UI.Tests;

public sealed class FloravoxEngineTests : IDisposable
{
    private readonly string? _voiceDir;

    public FloravoxEngineTests()
    {
        var modelsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceGardenSAPIAdapter", "models");
        if (!Directory.Exists(modelsRoot))
        {
            return;
        }
        // First piper voice dir with a *.onnx.json sidecar (routing key used
        // by the C++ adapter — see HasPiperSidecar in TTSEngine.cpp).
        _voiceDir = (from d in Directory.EnumerateDirectories(modelsRoot, "*", SearchOption.AllDirectories)
                     where Directory.EnumerateFiles(d, "*.onnx.json").Any()
                     let onnx = Directory.EnumerateFiles(d, "*.onnx").FirstOrDefault()
                     where onnx != null
                     select d).FirstOrDefault();
    }

    public void Dispose() { }

    private string? ModelsRoot => _voiceDir is null ? null
        : Path.GetFullPath(Path.Combine(_voiceDir, "..", ".."));

    private Dictionary<string, string> Creds(string modelId, string modelsRoot, bool withLang) =>
        withLang
            ? new() { ["modelId"] = modelId, ["modelsDir"] = modelsRoot, ["lang"] = "en" }
            : new() { ["modelId"] = modelId, ["modelsDir"] = modelsRoot };

    [Fact]
    public void MissingVoiceSurfacesAsSpeakError()
    {
        // Engine construction is lazy (the model is resolved on first
        // synthesis), so a nonexistent modelId must surface as a speak-time
        // exception — never a silent success or a crash. The C++ adapter's
        // sherpaonnx fallback keys off construction failures (engine absent
        // from the DLL), which this complements.
        using var c = new TtsClient("floravox", Creds("no-such-voice", ".", withLang: false));
        Assert.ThrowsAny<Exception>(() => c.SpeakSync("hello"));
    }

    [Fact]
    public void FloravoxSpeaksSsmlWithBoundariesAndMarks()
    {
        if (_voiceDir is null)
        {
            return; // no local voice — skip
        }

        var modelsRoot = ModelsRoot!.Replace('\\', '/');
        var modelId = _voiceDir!.Replace('\\', '/')[(modelsRoot.Length + 1)..];

        using var client = new TtsClient("floravox", Creds(modelId, modelsRoot, withLang: true));

        var audioBytes = 0L;
        var boundaries = new List<(string word, float start, float end, bool estimated)>();
        var marks = new List<(string name, float start)>();

        client.SetOnAudio(data => audioBytes += data.Length);
        client.SetOnBoundary((word, _, _, start, end, estimated) =>
            boundaries.Add((word, start, end, estimated)));
        client.SetOnMark((name, _, start, _) => marks.Add((name, start)));

        client.SpeakSync("<speak>Hello <mark name='vg1'/>world, floravox measures this.</speak>");

        Assert.True(audioBytes > 0, $"no audio produced ({audioBytes} bytes)");
        Assert.NotEmpty(boundaries);

        // Timings are monotonic and land inside the audio (they are scaled to
        // the synthesized length even for unpatched voices; voices patched
        // with floravox's duration-graph surgery additionally report
        // estimated == false).
        var starts = boundaries.Select(b => b.start).ToList();
        Assert.Equal(starts.OrderBy(s => s), starts);
        Assert.All(boundaries, b => Assert.True(b.start >= 0 && b.end > b.start));

        // The bookmark must fire as a mark event (mapped to SPEI_TTSBOOKMARK
        // by the C++ adapter).
        Assert.Contains(marks, m => m.name == "vg1");
    }

    [Fact]
    public void LexiconG2pHandlesOovWord()
    {
        // "floravox" is not in a gruut lexicon; the OOV chain (lexicon →
        // Phonetisaurus → ByT5 → spell) must still produce audio without
        // failing. Needs the lang bundle, which may require network on first
        // run — treated as skip if synthesis fails offline.
        if (_voiceDir is null)
        {
            return;
        }

        var modelsRoot = ModelsRoot!.Replace('\\', '/');
        var modelId = _voiceDir!.Replace('\\', '/')[(modelsRoot.Length + 1)..];

        using var client = new TtsClient("floravox", Creds(modelId, modelsRoot, withLang: true));
        var audioBytes = 0L;
        client.SetOnAudio(data => audioBytes += data.Length);
        try
        {
            client.SpeakSync("<speak>The floravox vocalizes.</speak>");
            Assert.True(audioBytes > 0, $"no audio for OOV sentence ({audioBytes} bytes)");
        }
        catch (Exception ex) when (ex.Message.Contains("lexicon", StringComparison.OrdinalIgnoreCase))
        {
            // First-run bundle fetch offline — acceptable skip.
        }
    }
}
