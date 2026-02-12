#pragma once

#include "SherpaOnnxConfig.h"
#include "SherpaOnnxDynamic.h"
#include <functional>
#include <memory>
#include <string>
#include <vector>

// Note: SherpaOnnx C API type forward declarations are in SherpaOnnxDynamic.h
// We use dynamic loading for the actual functions

namespace SherpaOnnx {

// C++ wrapper around SherpaOnnx C API for TTS operations
// Uses dynamic loading to avoid static library dependencies
class Engine {
public:
    explicit Engine(const ModelConfig& config);
    ~Engine();

    // Generate speech from text (returns float samples in [-1, 1] range)
    std::vector<float> Generate(const std::string& text, float speed = 1.0f);
    bool GenerateWithProgressCallback(
        const std::string& text,
        float speed,
        const std::function<bool(const float* samples, int32_t n, float progress)>& onChunk);

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
    const SherpaOnnxOfflineTts* m_tts = nullptr;
    ModelConfig m_config;
    std::string m_lastError;
    std::vector<std::string> m_ownedStrings;

    const char* PersistString(const std::string& value, bool nullIfEmpty = false);

    // Helper to convert SherpaOnnx audio to vector
    static std::vector<float> ConvertGeneratedAudio(
        const SherpaOnnxGeneratedAudio* audio);

    // Build C API config from our config structure
    SherpaOnnxOfflineTtsConfig BuildCApiConfig();

    // Validate model-type specific config/files before calling into Sherpa.
    bool ValidateConfig(std::string& error) const;
};

} // namespace SherpaOnnx
