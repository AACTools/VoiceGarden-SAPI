#pragma once

// Dynamic loading wrapper for SherpaOnnx C API
// This allows using SherpaOnnx without static linking to the libraries

#include <windows.h>
#include <string>
#include <memory>
#include <cstdint>

// SherpaOnnx C API type definitions
// These mirror the structures from sherpa-onnx/c-api/c-api.h
extern "C" {

// VITS Model Config
struct SherpaOnnxOfflineTtsVitsModelConfig {
    const char* model;
    const char* lexicon;
    const char* tokens;
    const char* data_dir;
    const char* dict_dir;
    float noise_scale;
    float noise_scale_w;
    float length_scale;
};

// Matcha Model Config
struct SherpaOnnxOfflineTtsMatchaModelConfig {
    const char* acoustic_model;
    const char* vocoder;
    const char* tokens;
    const char* lexicon;
    const char* data_dir;
    const char* dict_dir;
    float noise_scale;
    float length_scale;
};

// Kokoro Model Config
struct SherpaOnnxOfflineTtsKokoroModelConfig {
    const char* model;
    const char* voices;
    const char* tokens;
    const char* lexicon;
    const char* data_dir;
    const char* dict_dir;
    const char* lang;
    float length_scale;
};

// TTS Model Config (union of all model types)
struct SherpaOnnxOfflineTtsModelConfig {
    SherpaOnnxOfflineTtsVitsModelConfig vits;
    SherpaOnnxOfflineTtsMatchaModelConfig matcha;
    SherpaOnnxOfflineTtsKokoroModelConfig kokoro;
    int num_threads;
    int debug;
    const char* provider;
};

// TTS Config
struct SherpaOnnxOfflineTtsConfig {
    SherpaOnnxOfflineTtsModelConfig model;
    const char* rule_fsts;
    int max_num_sentences;
    const char* rule_fars;
    float silence_scale;
};

// Generated Audio
struct SherpaOnnxGeneratedAudio {
    const float* samples;
    int32_t n;
    int32_t sample_rate;
};

// Opaque TTS handle
struct SherpaOnnxOfflineTts;

} // extern "C"

namespace SherpaOnnx {
namespace Dynamic {

// Singleton class that manages SherpaOnnx DLL loading and function pointers
class SherpaOnnxLoader {
public:
    static SherpaOnnxLoader& Instance();

    // Initialize the loader (loads DLL and gets function pointers)
    bool Initialize();

    // Check if the loader is initialized
    bool IsInitialized() const { return m_hModule != nullptr; }

    // Get the last error message
    const std::string& GetLastError() const { return m_lastError; }

    // Function pointers to SherpaOnnx C API
    SherpaOnnxOfflineTts* (*SherpaOnnxCreateOfflineTts)(const SherpaOnnxOfflineTtsConfig*);
    void (*SherpaOnnxDestroyOfflineTts)(SherpaOnnxOfflineTts*);
    const SherpaOnnxGeneratedAudio* (*SherpaOnnxOfflineTtsGenerate)(
        SherpaOnnxOfflineTts*, const char*, int, float);
    void (*SherpaOnnxDestroyOfflineTtsGeneratedAudio)(const SherpaOnnxGeneratedAudio*);
    int (*SherpaOnnxOfflineTtsSampleRate)(const SherpaOnnxOfflineTts*);
    int (*SherpaOnnxOfflineTtsNumSpeakers)(const SherpaOnnxOfflineTts*);

private:
    SherpaOnnxLoader();
    ~SherpaOnnxLoader();

    // Prevent copying
    SherpaOnnxLoader(const SherpaOnnxLoader&) = delete;
    SherpaOnnxLoader& operator=(const SherpaOnnxLoader&) = delete;

    HMODULE m_hModule = nullptr;
    std::string m_lastError;

    // Get a function pointer by name
    template<typename T>
    bool GetFunction(const char* name, T& funcPtr) {
        funcPtr = reinterpret_cast<T>(GetProcAddress(m_hModule, name));
        if (!funcPtr) {
            m_lastError = "Failed to get function: ";
            m_lastError += name;
            return false;
        }
        return true;
    }
};

// Inline getter for the loader instance
inline SherpaOnnxLoader& Loader() {
    return SherpaOnnxLoader::Instance();
}

} // namespace Dynamic
} // namespace SherpaOnnx

// Override the SherpaOnnx C API functions to use dynamic loading
// These macros replace the original function calls
#define SherpaOnnxCreateOfflineTts SherpaOnnx::Dynamic::Loader().SherpaOnnxCreateOfflineTts
#define SherpaOnnxDestroyOfflineTts SherpaOnnx::Dynamic::Loader().SherpaOnnxDestroyOfflineTts
#define SherpaOnnxOfflineTtsGenerate SherpaOnnx::Dynamic::Loader().SherpaOnnxOfflineTtsGenerate
#define SherpaOnnxDestroyOfflineTtsGeneratedAudio SherpaOnnx::Dynamic::Loader().SherpaOnnxDestroyOfflineTtsGeneratedAudio
#define SherpaOnnxOfflineTtsSampleRate SherpaOnnx::Dynamic::Loader().SherpaOnnxOfflineTtsSampleRate
#define SherpaOnnxOfflineTtsNumSpeakers SherpaOnnx::Dynamic::Loader().SherpaOnnxOfflineTtsNumSpeakers
