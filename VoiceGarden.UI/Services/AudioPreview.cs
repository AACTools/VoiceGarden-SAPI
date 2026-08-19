using System;
using System.IO;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Shared voice-preview helper: wraps raw PCM16 bytes in a WAV header and
/// plays them through SoundPlayer on a background thread.
/// </summary>
public static class AudioPreview
{
    /// <summary>
    /// Wrap raw PCM16 mono samples in a WAV header so SoundPlayer can play them.
    /// Rust's SynthToBytes returns raw PCM16, not WAV.
    /// </summary>
    public static byte[] WrapPcmInWav(byte[] pcm, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        short channels = 1;
        short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int dataLen = pcm.Length;
        int riffLen = 36 + dataLen;

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(riffLen);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // PCM chunk size
        bw.Write((short)1); // PCM format
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataLen);
        bw.Write(pcm);
        return ms.ToArray();
    }

    /// <summary>
    /// Play raw PCM16 mono audio on a background thread. The temp file is
    /// deleted after playback. Fire-and-forget.
    /// </summary>
    public static void PlayPcm(byte[] pcm, int sampleRate, string tempPrefix = "vg_preview_")
    {
        var wavData = WrapPcmInWav(pcm, sampleRate);
        var tempFile = Path.Combine(Path.GetTempPath(), $"{tempPrefix}{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(tempFile, wavData);
        }
        catch
        {
            return; // cannot even write the temp file — nothing to play
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try { using var player = new System.Media.SoundPlayer(tempFile); player.PlaySync(); }
            catch { }
            finally { try { File.Delete(tempFile); } catch { } }
        });
    }

    /// <summary>
    /// Language-appropriate preview text for a SherpaOnnx model. MMS models
    /// are character-based and only recognize characters from their target
    /// language script.
    /// </summary>
    public static string GetSherpaPreviewText(string modelId, string modelName)
    {
        var id = modelId.ToLowerInvariant();

        // English models — use English
        if (id.Contains("eng") || id.StartsWith("piper-en") || id.StartsWith("kokoro-en"))
            return $"Hello, this is a {modelName} voice.";

        // MMS models — extract the ISO 639-3 code and try a native greeting
        if (id.StartsWith("mms_"))
        {
            var langCode = id.Substring(4); // e.g., "fas", "hyw", "ara"
            return langCode switch
            {
                "fas" => "سلام، این یک صدای فارسی است.",           // Persian
                "ara" => "مرحبا، هذه تجربة صوتية.",                // Arabic
                "hyw" or "hye" => "Բարև, սա ձայնային փորձարկում է:", // Armenian
                "hin" => "नमस्ते, यह एक आवाज परीक्षण है।",            // Hindi
                "ben" => "হ্যালো, এটি একটি ভয়েস পরীক্ষা।",           // Bengali
                "urd" => "ہیلو، یہ ایک آواز کا ٹیسٹ ہے۔",              // Urdu
                "rus" => "Привет, это тестовое озвучивание.",         // Russian
                "zho" or "cmn" => "你好，这是一个语音测试。",           // Chinese
                "jpn" => "こんにちは、これは音声テストです。",          // Japanese
                "kor" => "안녕하세요, 음성 테스트입니다.",              // Korean
                "tur" => "Merhaba, bu bir ses testidir.",             // Turkish
                "vie" => "Xin chào, đây là một bài kiểm tra giọng nói.", // Vietnamese
                "tha" => "สวัสดีนี่คือการทดสอบเสียงพูด",              // Thai
                "fra" or "fre" => "Bonjour, ceci est un test vocal.", // French
                "deu" or "ger" => "Hallo, dies ist ein Sprachtest.",  // German
                "spa" => "Hola, esta es una prueba de voz.",         // Spanish
                "por" => "Olá, este é um teste de voz.",             // Portuguese
                "ita" => "Ciao, questo è un test vocale.",           // Italian
                "guj" => "નમસ્તે, આ એક અવાજ ચકાસણી છે.",               // Gujarati
                _ => $"[test] {langCode}", // Fallback — may produce no audio
            };
        }

        // Piper/Kokoro non-English — try English (Piper models often support it)
        return $"Hello. {modelName}.";
    }
}
