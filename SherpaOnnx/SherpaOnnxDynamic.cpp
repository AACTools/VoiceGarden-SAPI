#include "SherpaOnnxDynamic.h"
#include <vector>
#include <array>
#include <filesystem>
#include <sstream>
#include <cstring>
#include <fstream>
#include <mutex>

namespace SherpaOnnx {
namespace Dynamic {
namespace {
std::mutex g_loaderInitMutex;

std::string GetCurrentModuleDirectory()
{
    HMODULE module = nullptr;
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            reinterpret_cast<LPCSTR>(&GetCurrentModuleDirectory),
                            &module)) {
        return {};
    }

    std::array<char, MAX_PATH> path{};
    DWORD len = GetModuleFileNameA(module, path.data(), static_cast<DWORD>(path.size()));
    if (len == 0 || len >= path.size()) {
        return {};
    }

    std::filesystem::path p(path.data());
    return p.parent_path().string();
}

std::string GetModulePath(HMODULE module)
{
    if (!module) {
        return {};
    }

    std::array<char, MAX_PATH> path{};
    DWORD len = GetModuleFileNameA(module, path.data(), static_cast<DWORD>(path.size()));
    if (len == 0 || len >= path.size()) {
        return {};
    }
    return std::string(path.data(), len);
}

std::string ToHexError(DWORD err)
{
    std::ostringstream oss;
    oss << "0x" << std::hex << err;
    return oss.str();
}

void AppendLoaderLogTo(const std::filesystem::path& filePath, const std::string& line)
{
    std::error_code ec;
    std::filesystem::create_directories(filePath.parent_path(), ec);
    std::ofstream out(filePath.string(), std::ios::app);
    if (!out.is_open()) {
        return;
    }
    out << line << "\n";
}

void AppendLoaderLog(const std::string& line)
{
    char appData[MAX_PATH] = {};
    DWORD len = GetEnvironmentVariableA("LOCALAPPDATA", appData, MAX_PATH);
    if (len != 0 && len < MAX_PATH) {
        std::filesystem::path logPath(appData);
        logPath /= "NaturalVoiceSAPIAdapter";
        logPath /= "sherpa_loader.log";
        AppendLoaderLogTo(logPath, line);
    }

    std::string moduleDir = GetCurrentModuleDirectory();
    if (!moduleDir.empty()) {
        AppendLoaderLogTo(std::filesystem::path(moduleDir) / "sherpa_loader.log", line);
    }

    char tempPath[MAX_PATH] = {};
    DWORD tempLen = GetTempPathA(MAX_PATH, tempPath);
    if (tempLen != 0 && tempLen < MAX_PATH) {
        AppendLoaderLogTo(std::filesystem::path(tempPath) / "NaturalVoiceSAPIAdapter-sherpa_loader.log", line);
    }
}
}

SherpaOnnxLoader::SherpaOnnxLoader()
{
    // Initialize all function pointers to null
    SherpaOnnxCreateOfflineTts = nullptr;
    SherpaOnnxDestroyOfflineTts = nullptr;
    SherpaOnnxOfflineTtsGenerate = nullptr;
    SherpaOnnxOfflineTtsGenerateWithProgressCallbackWithArg = nullptr;
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
    std::lock_guard<std::mutex> guard(g_loaderInitMutex);

    if (m_hModule) {
        return true; // Already initialized
    }

    // Prefer loading from the same directory as this module (adapter DLL / smoke test EXE).
    std::string moduleDir = GetCurrentModuleDirectory();
    AppendLoaderLog(std::string("[loader] moduleDir=") + moduleDir);
    if (!moduleDir.empty()) {
        const std::string ortPath = moduleDir + "\\onnxruntime.dll";
        HMODULE existingOrt = GetModuleHandleA("onnxruntime.dll");
        if (existingOrt) {
            std::string existingPath = GetModulePath(existingOrt);
            AppendLoaderLog(std::string("[loader] existing onnxruntime.dll path=") + existingPath);
            if (existingPath.empty()) {
                m_lastError = "onnxruntime.dll is already loaded but module path could not be resolved.";
                AppendLoaderLog(std::string("[loader] ERROR: ") + m_lastError);
                return false;
            }
            if (_stricmp(existingPath.c_str(), ortPath.c_str()) != 0) {
                m_lastError = "Conflicting onnxruntime.dll already loaded from: ";
                m_lastError += existingPath;
                m_lastError += ". Expected: ";
                m_lastError += ortPath;
                AppendLoaderLog(std::string("[loader] ERROR: ") + m_lastError);
                return false;
            }
        }
        else {
            AppendLoaderLog(std::string("[loader] preloading onnxruntime from ") + ortPath);
            HMODULE ortModule = LoadLibraryExA(ortPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
            if (!ortModule) {
                m_lastError = "Failed to preload onnxruntime.dll from ";
                m_lastError += ortPath;
                m_lastError += " (error ";
                m_lastError += ToHexError(::GetLastError());
                m_lastError += ").";
                AppendLoaderLog(std::string("[loader] ERROR: ") + m_lastError);
                return false;
            }
            AppendLoaderLog(std::string("[loader] preloaded onnxruntime handle path=") + GetModulePath(ortModule));
        }

        std::string explicitPath = moduleDir + "\\sherpa-onnx-c-api.dll";
        AppendLoaderLog(std::string("[loader] loading sherpa dll from ") + explicitPath);
        m_hModule = LoadLibraryExA(explicitPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    }

    if (!m_hModule) {
        // Fallback: search by name.
        m_hModule = LoadLibraryA("sherpa-onnx-c-api.dll");
    }

    if (!m_hModule) {
        m_lastError = "Failed to load sherpa-onnx-c-api.dll. ";
        m_lastError += "Please ensure the SherpaOnnx DLLs are in the application directory.";
        AppendLoaderLog(std::string("[loader] ERROR: ") + m_lastError);
        return false;
    }
    AppendLoaderLog(std::string("[loader] sherpa module path=") + GetModulePath(m_hModule));

    // Get all function pointers
    bool success = true;
    success &= GetFunction("SherpaOnnxCreateOfflineTts", SherpaOnnxCreateOfflineTts);
    success &= GetFunction("SherpaOnnxDestroyOfflineTts", SherpaOnnxDestroyOfflineTts);
    success &= GetFunction("SherpaOnnxOfflineTtsGenerate", SherpaOnnxOfflineTtsGenerate);
    SherpaOnnxOfflineTtsGenerateWithProgressCallbackWithArg =
        reinterpret_cast<decltype(SherpaOnnxOfflineTtsGenerateWithProgressCallbackWithArg)>(
            GetProcAddress(m_hModule, "SherpaOnnxOfflineTtsGenerateWithProgressCallbackWithArg"));
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
