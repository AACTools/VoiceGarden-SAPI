#pragma once

// Dynamic loading wrapper for SherpaOnnx C API
// This allows using SherpaOnnx without static linking to the libraries

#include <windows.h>
#include <string>
#include <memory>

// Forward declarations for SherpaOnnx C API types
extern "C" {

// Forward declarations for SherpaOnnx Offline TTS types
struct SherpaOnnxOfflineTtsVitsModelConfig;
struct SherpaOnnxOfflineTtsMatchaModelConfig;
struct SherpaOnnxOfflineTtsKokoroModelConfig;
struct SherpaOnnxOfflineTtsModelConfig;
struct SherpaOnnxOfflineTtsConfig;
struct SherpaOnnxGeneratedAudio;
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
