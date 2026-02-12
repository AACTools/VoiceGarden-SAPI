#pragma once

// Dynamic loading wrapper for SherpaOnnx C API
// This allows using SherpaOnnx without static linking to the libraries

#include <windows.h>
#include <string>
#include <memory>
#include <cstdint>
#include <functional>

extern "C" {
#include "../SherpaOnnx/libs/sherpa-onnx-v1.12.23-win-x64-shared/include/sherpa-onnx/c-api/c-api.h"
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
    const SherpaOnnxOfflineTts* (*SherpaOnnxCreateOfflineTts)(const SherpaOnnxOfflineTtsConfig*);
    void (*SherpaOnnxDestroyOfflineTts)(const SherpaOnnxOfflineTts*);
    const SherpaOnnxGeneratedAudio* (*SherpaOnnxOfflineTtsGenerate)(
        const SherpaOnnxOfflineTts*, const char*, int, float);
    const SherpaOnnxGeneratedAudio* (*SherpaOnnxOfflineTtsGenerateWithProgressCallbackWithArg)(
        const SherpaOnnxOfflineTts*, const char*, int, float, SherpaOnnxGeneratedAudioProgressCallbackWithArg, void*);
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
