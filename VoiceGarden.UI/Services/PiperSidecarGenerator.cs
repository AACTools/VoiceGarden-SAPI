using System.Text;
using System.Text.Json;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Generates piper-style sidecar configs (<c>X.onnx.json</c>) for local
/// voices that ship in sherpa layout (<c>model.onnx</c> + <c>tokens.txt</c>)
/// so the floravox engine can load them (issue #15, SPD installer parity).
///
/// Ports the SPD hardening rules:
///  - sidecar is re-generated when <c>tokens.txt</c> is newer (models updated
///    in place must never keep a stale phoneme map),
///  - <c>phoneme_id_map</c> keys are case-folded (the G2P chain looks up
///    phonemes case-insensitively; sherpa token tables ship single-case),
///  - a <c>language.code</c> sidecar is written when derivable from the
///    model id (floravox 0.8.5 uses it for language routing).
/// </summary>
public static class PiperSidecarGenerator
{
    /// <summary>
    /// Ensure a sidecar exists (and is fresh) next to the model in
    /// <paramref name="modelDir"/>. Returns the sidecar path when one was
    /// written, null when nothing needed doing (or the layout is not
    /// supported). Never throws.
    /// </summary>
    /// <param name="modelDir">Directory holding the .onnx + tokens.txt.</param>
    /// <param name="modelId">Canonical model id (e.g. "mms_eng", "piper-en_US-amy-low").</param>
    /// <param name="sampleRate">Sample rate when known (catalog); otherwise read from a sibling config.json, else omitted.</param>
    public static string? EnsureSidecar(string modelDir, string modelId, int? sampleRate = null)
    {
        try
        {
            // Flow/diffusion families (zipvoice, supertonic, pocket, kitten)
            // are not floravox graphs - they carry tokens.txt too, so they
            // must be excluded by name before any layout check.
            if (IsFlowModelFamily(modelId))
                return null;

            var onnx = FindModelOnnx(modelDir);
            if (onnx is null) return null;

            var tokensPath = Path.Combine(modelDir, "tokens.txt");
            if (!File.Exists(tokensPath)) return null;

            // Kokoro voices don't need a generated sidecar: floravox's
            // KokoroBackend reads tokens.txt and voices.bin directly from
            // the model dir.
            if (File.Exists(Path.Combine(modelDir, "voices.bin"))) return null;

            var sidecarPath = onnx + ".json";
            if (File.Exists(sidecarPath))
            {
                // Only OUR generated sidecars are refreshed. A shipped or
                // patched sidecar (piper releases, floravox duration-surgery
                // output) is authoritative — regenerating it from tokens.txt
                // would drop espeak/inference/audio config it carries.
                if (!WasGeneratedByUs(sidecarPath))
                    return null;

                var sidecarTime = File.GetLastWriteTimeUtc(sidecarPath);
                var tokensTime = File.GetLastWriteTimeUtc(tokensPath);
                if (sidecarTime >= tokensTime)
                    return null; // fresh — nothing to do
            }

            var map = ParseTokensFile(tokensPath);
            if (map.Count == 0) return null;

            // Case-fold keys: add the invariant-folded variant of every
            // symbol when it is not already present (SPD parity — G2P looks
            // up phonemes case-insensitively).
            var folded = new Dictionary<string, List<long>>(map.Count);
            foreach (var (sym, ids) in map)
            {
                folded[sym] = ids;
                var lower = sym.ToLowerInvariant();
                if (lower != sym && !map.ContainsKey(lower))
                    folded.TryAdd(lower, ids);
            }

            sampleRate ??= ReadSampleRateFromConfigJson(modelDir);

            var language = DeriveLanguageCode(modelId);

            var json = BuildSidecarJson(folded, sampleRate, language);
            WriteAtomically(sidecarPath, json);
            return sidecarPath;
        }
        catch
        {
            // Sidecar generation is best-effort: the sherpa-onnx engine
            // still works without it.
            return null;
        }
    }

    /// <summary>Marker key written into generated sidecars (never shipped ones).</summary>
    private const string GeneratorMarker = "voicegardenGenerated";

    /// <summary>
    /// Flow/diffusion model families that floravox cannot load (its backends
    /// are piper/MMS VITS, Matcha + vocoder, Kokoro). They carry tokens.txt
    /// like every sherpa model, so exclusion is by name.
    /// </summary>
    internal static bool IsFlowModelFamily(string modelId)
    {
        var id = modelId.ToLowerInvariant();
        return id.Contains("zipvoice") || id.Contains("supertonic")
            || id.Contains("pocket") || id.Contains("kitten");
    }

    private static bool WasGeneratedByUs(string sidecarPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(sidecarPath));
            return doc.RootElement.TryGetProperty(GeneratorMarker, out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The main acoustic model (prefers model.onnx, skips vocoders).</summary>
    private static string? FindModelOnnx(string modelDir)
    {
        if (!Directory.Exists(modelDir)) return null;
        var candidates = Directory.GetFiles(modelDir, "*.onnx");
        return candidates.FirstOrDefault(f =>
                   string.Equals(Path.GetFileName(f), "model.onnx", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(f =>
                   !Path.GetFileName(f).Contains("vocoder", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// sherpa tokens.txt: one symbol per line followed by one or more ids
    /// ("<c>symbol id</c>", "<c>symbol id1 id2</c>"). The first line may be a
    /// count header ("<c>32</c>") — entries whose "symbol" is all digits and
    /// which have no ids are skipped.
    /// </summary>
    internal static Dictionary<string, List<long>> ParseTokensFile(string tokensPath)
    {
        var map = new Dictionary<string, List<long>>();
        foreach (var rawLine in File.ReadAllLines(tokensPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var space = line.IndexOf(' ');
            if (space <= 0) continue; // count header or malformed

            var symbol = line[..space];
            var rest = line[(space + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length == 0) continue;
            if (symbol.All(char.IsAsciiDigit)) continue; // header line

            var ids = new List<long>(rest.Length);
            var ok = true;
            foreach (var t in rest)
            {
                if (long.TryParse(t, out var id)) ids.Add(id);
                else { ok = false; break; }
            }
            if (!ok) continue;

            if (map.TryGetValue(symbol, out var existing)) existing.AddRange(ids.Where(id => !existing.Contains(id)));
            else map[symbol] = ids;
        }
        return map;
    }

    /// <summary>MMS training configs carry data.sampling_rate.</summary>
    private static int? ReadSampleRateFromConfigJson(string modelDir)
    {
        var configPath = Path.Combine(modelDir, "config.json");
        if (!File.Exists(configPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("sampling_rate", out var rate) &&
                rate.TryGetInt32(out var sr))
                return sr;
        }
        catch { /* best effort */ }
        return null;
    }

    /// <summary>
    /// Derives a BCP-47-ish language code from the model id:
    /// piper-en_US-amy-low → en-US, mms_eng → en. Empty when unknown.
    /// </summary>
    internal static string DeriveLanguageCode(string modelId)
    {
        // Piper ids are dash-separated with an underscore locale
        // ("piper-en_US-amy-low" → en-US); MMS ids are underscore-separated
        // ("mms_eng" → en).
        // Family = everything before the first '-' or '_' ("piper-en_US-…"
        // → piper, "mms_eng" → mms).
        var sep = modelId.IndexOfAny(new[] { '-', '_' });
        var family = (sep < 0 ? modelId : modelId[..sep]).ToLowerInvariant();

        if (family == "piper")
        {
            var locale = modelId.Split('-', 3); // [piper, en_US, name…]
            if (locale.Length > 1 && locale[1].Length > 0)
                return locale[1].Replace('_', '-');
        }
        else if (family == "mms")
        {
            var mms = modelId.Split('_'); // [mms, eng]
            if (mms.Length > 1)
            {
                return mms[1].ToLowerInvariant() switch
                {
                "eng" => "en",
                "fas" or "pes" => "fa",
                "arb" => "ar",
                "spa" => "es",
                "fra" => "fr",
                "deu" => "de",
                "por" => "pt",
                "ita" => "it",
                "rus" => "ru",
                "zho" => "zh",
                "hin" => "hi",
                _ => "",
                };
            }
        }
        return "";
    }

    private static string BuildSidecarJson(Dictionary<string, List<long>> phonemeMap, int? sampleRate, string language)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            // Marker so future runs can tell our minimal sidecars from
            // shipped/patched ones (only ours are ever refreshed).
            writer.WriteBoolean(GeneratorMarker, true);

            if (sampleRate.HasValue || language.Length > 0)
            {
                writer.WriteStartObject("audio");
                if (sampleRate.HasValue) writer.WriteNumber("sample_rate", sampleRate.Value);
                writer.WriteEndObject();

                if (language.Length > 0)
                {
                    writer.WriteStartObject("language");
                    writer.WriteString("code", language);
                    writer.WriteEndObject();
                }
            }

            writer.WriteNumber("num_speakers", 1);

            writer.WriteStartObject("phoneme_id_map");
            foreach (var (symbol, ids) in phonemeMap.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                writer.WriteStartArray(symbol);
                foreach (var id in ids) writer.WriteNumberValue(id);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteAtomically(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
