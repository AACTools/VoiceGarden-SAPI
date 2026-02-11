#pragma once

#include "SherpaOnnxConfig.h"
#include "SherpaOnnxDynamic.h"
#include <memory>
#include <string>
#include <vector>

// Include SherpaOnnx C API type definitions only
// We'll use dynamic loading for the actual functions
extern "C" {
#include "sherpa-onnx/c-api/c-api.h"
}

namespace SherpaOnnx {

// C++ wrapper around SherpaOnnx C API for TTS operations
// Uses dynamic loading to avoid static library dependencies
class Engine {
public:
    explicit Engine(const ModelConfig& config);
    ~Engine();

    // Generate speech from text (returns float samples in [-1, 1] range)
    std::vector<float> Generate(const std::string& text, float speed = 1.0f);

    // Get engine properties
    int GetSampleRate() const;
    int GetNumSpeakers() const;
    bool IsValid() const { return m_tts != nullptr; }
    const std::string& GetLastError() const { return m_lastError; }
    const std::string& GetVoiceName() const { return m_config.voiceName; }

    // Disable copy
    Engine(const Engine&) = delete;
    Engine& operator=(const Engine&) = delete;

private:
    SherpaOnnxOfflineTts* m_tts = nullptr;
    ModelConfig m_config;
    std::string m_lastError;

    // Helper to convert SherpaOnnx audio to vector
    static std::vector<float> ConvertGeneratedAudio(
        const SherpaOnnxGeneratedAudio* audio);

    // Build C API config from our config structure
    SherpaOnnxOfflineTtsConfig BuildCApiConfig();
};

} // namespace SherpaOnnx
