#include "pch.h"
#include "RustTtsLoader.h"
#include <spdlog/spdlog.h>

namespace RustTts {

Loader& Loader::Instance() {
    static Loader instance;
    return instance;
}

Loader::Loader() = default;
Loader::~Loader() {
    if (m_hModule) {
        FreeLibrary(m_hModule);
        m_hModule = nullptr;
    }
}

template<typename T>
bool Loader::GetFunc(const char* name, T& funcPtr) {
    funcPtr = reinterpret_cast<T>(GetProcAddress(m_hModule, name));
    if (!funcPtr) {
        m_lastError = std::string("Failed to get function: ") + name;
        return false;
    }
    return true;
}

bool Loader::Initialize() {
    if (m_hModule) return true; // already loaded

    // Try multiple locations: exe directory, system paths
    const char* dllName = "tts_wrapper.dll";

    // Search alongside the adapter DLL itself
    wchar_t modulePath[MAX_PATH] = {};
    GetModuleFileNameW(GetModuleHandleW(L"VoiceGardenSAPIAdapter"), modulePath, MAX_PATH);
    std::filesystem::path dir = std::filesystem::path(modulePath).parent_path();

    // Try x64/x86 subdirs (MSI layout) then the same dir (flat layout)
    std::vector<std::filesystem::path> candidates = {
        dir / dllName,
        dir / "x64" / dllName,
        dir / "x86" / dllName,
    };

    for (const auto& path : candidates) {
        if (std::filesystem::exists(path)) {
            m_hModule = LoadLibraryW(path.wstring().c_str());
            if (m_hModule) {
                spdlog::info("RustTts: loaded {}", path.string());
                break;
            }
        }
    }

    // Fallback to default search path
    if (!m_hModule) {
        m_hModule = LoadLibraryA(dllName);
        if (m_hModule) {
            spdlog::info("RustTts: loaded from system path");
        }
    }

    if (!m_hModule) {
        m_lastError = "tts_wrapper.dll not found";
        spdlog::info("RustTts: tts_wrapper.dll not found, using fallback engines");
        return false;
    }

    // Resolve all function pointers
    bool ok = true;
    ok &= GetFunc("tts_create", create);
    ok &= GetFunc("tts_destroy", destroy);
    ok &= GetFunc("tts_speak", speak);
    ok &= GetFunc("tts_speak_ssml", speakSsml);
    ok &= GetFunc("tts_speak_sync", speakSync);
    ok &= GetFunc("tts_stop", stop);
    ok &= GetFunc("tts_set_voice", setVoice);
    ok &= GetFunc("tts_set_rate", setRate);
    ok &= GetFunc("tts_set_pitch", setPitch);
    ok &= GetFunc("tts_set_volume", setVolume);
    ok &= GetFunc("tts_set_on_audio", setOnAudio);
    ok &= GetFunc("tts_set_on_boundary2", setOnBoundary2);
    ok &= GetFunc("tts_set_on_viseme", setOnViseme);
    ok &= GetFunc("tts_set_on_start", setOnStart);
    ok &= GetFunc("tts_set_on_end", setOnEnd);
    ok &= GetFunc("tts_set_on_error", setOnError);
    ok &= GetFunc("tts_get_last_error", getLastError);

    if (!ok) {
        spdlog::warn("RustTts: failed to resolve all functions: {}", m_lastError);
        FreeLibrary(m_hModule);
        m_hModule = nullptr;
        return false;
    }

    spdlog::info("RustTts: all function pointers resolved");
    return true;
}

} // namespace RustTts
