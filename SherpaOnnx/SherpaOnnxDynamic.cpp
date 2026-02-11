#include "SherpaOnnxDynamic.h"
#include <vector>

namespace SherpaOnnx {
namespace Dynamic {

SherpaOnnxLoader::SherpaOnnxLoader()
{
    // Initialize all function pointers to null
    SherpaOnnxCreateOfflineTts = nullptr;
    SherpaOnnxDestroyOfflineTts = nullptr;
    SherpaOnnxOfflineTtsGenerate = nullptr;
    SherpaOnnxDestroyOfflineTtsGeneratedAudio = nullptr;
    SherpaOnnxOfflineTtsSampleRate = nullptr;
    SherpaOnnxOfflineTtsNumSpeakers = nullptr;
}

SherpaOnnxLoader::~SherpaOnnxLoader()
{
    // Free the DLL module
    if (m_hModule) {
        FreeLibrary(m_hModule);
        m_hModule = nullptr;
    }
}

SherpaOnnxLoader& SherpaOnnxLoader::Instance()
{
    static SherpaOnnxLoader instance;
    return instance;
}

bool SherpaOnnxLoader::Initialize()
{
    if (m_hModule) {
        return true; // Already initialized
    }

    // Try to load the DLL from the same directory as our executable
    // First, try sherpa-onnx-c-api.dll
    m_hModule = LoadLibraryExA("sherpa-onnx-c-api.dll", nullptr, LOAD_LIBRARY_SEARCH_APPLICATION_DIR);

    if (!m_hModule) {
        // Fallback: try loading from system paths
        m_hModule = LoadLibraryA("sherpa-onnx-c-api.dll");
    }

    if (!m_hModule) {
        m_lastError = "Failed to load sherpa-onnx-c-api.dll. ";
        m_lastError += "Please ensure the SherpaOnnx DLLs are in the application directory.";
        return false;
    }

    // Get all function pointers
    bool success = true;
    success &= GetFunction("SherpaOnnxCreateOfflineTts", SherpaOnnxCreateOfflineTts);
    success &= GetFunction("SherpaOnnxDestroyOfflineTts", SherpaOnnxDestroyOfflineTts);
    success &= GetFunction("SherpaOnnxOfflineTtsGenerate", SherpaOnnxOfflineTtsGenerate);
    success &= GetFunction("SherpaOnnxDestroyOfflineTtsGeneratedAudio", SherpaOnnxDestroyOfflineTtsGeneratedAudio);
    success &= GetFunction("SherpaOnnxOfflineTtsSampleRate", SherpaOnnxOfflineTtsSampleRate);
    success &= GetFunction("SherpaOnnxOfflineTtsNumSpeakers", SherpaOnnxOfflineTtsNumSpeakers);

    if (!success) {
        // Clean up on failure
        FreeLibrary(m_hModule);
        m_hModule = nullptr;
        return false;
    }

    return true;
}

} // namespace Dynamic
} // namespace SherpaOnnx
