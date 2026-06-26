#pragma once
#include <string>
#include <functional>
#include <memory>
#include <cstdint>

// Generic HTTP-based TTS synthesis for cloud engines.
// Supports OpenAI, ElevenLabs, Google, Cartesia, Deepgram via HTTP POST.
// Audio is decoded from MP3 to PCM and delivered via callback.

class GenericHttpTts
{
public:
    using AudioCallback = std::function<int(const uint8_t* data, uint32_t len)>;

    GenericHttpTts();
    ~GenericHttpTts();

    // Configure for a specific engine
    void SetEngine(const std::string& engineType, const std::string& key,
                   const std::string& voice, const std::string& region = {});

    // Synthesize text to audio, delivering PCM via callback
    void Speak(const std::string& text, AudioCallback audioCallback);

    // Cancel ongoing synthesis
    void Stop() { m_abort = true; }

private:
    struct EngineRequest
    {
        std::string url;
        std::string body;
        std::string contentType;
        std::string headers;
        bool isBase64Response = false;
    };

    EngineRequest BuildRequest(const std::string& text) const;
    static std::string JsonEscape(const std::string& s);

    std::string m_engineType;
    std::string m_key;
    std::string m_voice;
    std::string m_region;
    std::atomic<bool> m_abort{false};
};
