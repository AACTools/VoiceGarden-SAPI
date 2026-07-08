#include "pch.h"
#include "GenericHttpTts.h"
#include "NetUtils.h"
#include "Mp3Decoder.h"
#include "Logger.h"

GenericHttpTts::GenericHttpTts() = default;
GenericHttpTts::~GenericHttpTts() = default;

void GenericHttpTts::SetEngine(const std::string& engineType, const std::string& key,
                               const std::string& voice, const std::string& region)
{
    m_engineType = engineType;
    m_key = key;
    m_voice = voice;
    m_region = region;
}

std::string GenericHttpTts::JsonEscape(const std::string& s)
{
    std::string out;
    out.reserve(s.size() + 8);
    for (char c : s)
    {
        switch (c)
        {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (static_cast<unsigned char>(c) < 0x20)
            {
                char buf[8];
                snprintf(buf, sizeof(buf), "\\u%04x", c);
                out += buf;
            }
            else
                out += c;
        }
    }
    return out;
}

GenericHttpTts::EngineRequest GenericHttpTts::BuildRequest(const std::string& text) const
{
    EngineRequest req;
    std::string escapedText = JsonEscape(text);

    if (m_engineType == "OpenAI")
    {
        req.url = "https://api.openai.com/v1/audio/speech";
        req.contentType = "application/json";
        req.headers = "Authorization: Bearer " + m_key + "\r\n";
        req.body = "{\"model\":\"tts-1\",\"input\":\"" + escapedText +
                   "\",\"voice\":\"" + m_voice + "\",\"response_format\":\"mp3\"}";
    }
    else if (m_engineType == "ElevenLabs")
    {
        req.url = "https://api.elevenlabs.io/v1/text-to-speech/" + m_voice;
        req.contentType = "application/json";
        req.headers = "xi-api-key: " + m_key + "\r\n";
        req.body = "{\"text\":\"" + escapedText +
                   "\",\"model_id\":\"eleven_multilingual_v2\","
                   "\"voice_settings\":{\"stability\":0.5,\"similarity_boost\":0.75}}";
    }
    else if (m_engineType == "Google")
    {
        req.url = "https://texttospeech.googleapis.com/v1/text:synthesize";
        req.contentType = "application/json";
        req.headers = "x-goog-api-key: " + m_key + "\r\n";
        req.body = "{\"input\":{\"text\":\"" + escapedText + "\"},"
                   "\"voice\":{\"languageCode\":\"en-US\",\"name\":\"" + m_voice + "\"},"
                   "\"audioConfig\":{\"audioEncoding\":\"MP3\",\"speakingRate\":1.0}}";
        req.isBase64Response = true;
    }
    else if (m_engineType == "Cartesia")
    {
        req.url = "https://api.cartesia.ai/tts/bytes";
        req.contentType = "application/json";
        req.headers = "X-API-Key: " + m_key + "\r\n";
        req.body = "{\"transcript\":\"" + escapedText +
                   "\",\"voice_id\":\"" + m_voice + "\","
                   "\"output_format\":{\"container\":\"raw\",\"encoding\":\"pcm_s16le\",\"sample_rate\":24000}}";
    }
    else if (m_engineType == "Deepgram")
    {
        req.url = "https://api.deepgram.com/v1/speak?model=" + m_voice;
        req.contentType = "application/json";
        req.headers = "Authorization: Token " + m_key + "\r\n";
        req.body = "{\"text\":\"" + escapedText + "\"}";
    }
    else
    {
        throw std::invalid_argument("Unsupported engine type: " + m_engineType);
    }

    return req;
}

void GenericHttpTts::Speak(const std::string& text, AudioCallback audioCallback)
{
    m_abort = false;

    auto req = BuildRequest(text);

    LogInfo("HTTP TTS: {} synthesizing {} chars", m_engineType, text.size());

    // Make HTTP POST
    auto audioData = PostToBytes(req.url, req.body, req.contentType, req.headers);

    if (m_abort) return;

    if (audioData.empty())
        throw std::runtime_error("HTTP TTS: empty response from " + m_engineType);

    LogInfo("HTTP TTS: received {} bytes", audioData.size());

    // Handle Google's base64-in-JSON response
    if (req.isBase64Response)
    {
        // Parse JSON to extract audioContent
        std::string json(audioData.begin(), audioData.end());
        size_t pos = json.find("\"audioContent\"");
        if (pos == std::string::npos)
            throw std::runtime_error("HTTP TTS: no audioContent in Google response");
        pos = json.find('"', pos + 14) + 1;
        size_t end = json.find('"', pos);
        std::string b64 = json.substr(pos, end - pos);

        // Decode base64
        static const std::string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        std::vector<uint8_t> decoded;
        decoded.reserve(b64.size() * 3 / 4);
        int val = 0, bits = 0;
        for (char c : b64)
        {
            if (c == '=' || c == '\n' || c == '\r' || c == ' ') break;
            auto p = chars.find(c);
            if (p == std::string::npos) continue;
            val = (val << 6) | static_cast<int>(p);
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                decoded.push_back(static_cast<uint8_t>((val >> bits) & 0xFF));
            }
        }
        audioData = std::move(decoded);
        LogInfo("HTTP TTS: decoded base64 to {} bytes", audioData.size());
    }

    // Check if raw PCM (Cartesia) or needs MP3 decode
    bool isPcm = (m_engineType == "Cartesia");

    if (isPcm)
    {
        // Raw 16-bit PCM — deliver directly
        if (!audioData.empty())
            audioCallback(audioData.data(), static_cast<uint32_t>(audioData.size()));
    }
    else
    {
        // MP3 — decode to PCM
        Mp3Decoder decoder;
        auto& waveFormat = decoder.GetWaveFormat();
        decoder.Convert(audioData.data(), static_cast<uint32_t>(audioData.size()),
            [&audioCallback, &waveFormat](const uint8_t* data, uint32_t len) {
                audioCallback(data, len);
            });
    }

    LogInfo("HTTP TTS: synthesis complete");
}
