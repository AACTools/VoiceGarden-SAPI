#pragma once

// Dynamic loading wrapper for rust-tts-wrapper (tts_wrapper.dll)
// Mirrors SherpaOnnxDynamic.h: LoadLibrary + GetProcAddress pattern.
// If the DLL fails to load, IsLoaded() returns false and the adapter
// falls back to its built-in GenericHttpTts / SpeechRestAPI paths.

#include <windows.h>
#include <string>
#include <cstdint>

// C ABI types from tts_wrapper.h
struct tts_ctx;
struct tts_voice;
struct tts_engine_info;

typedef void (*CAudioCb)(const uint8_t*, uintptr_t, void*);
typedef void (*CBoundaryCb2)(const char*, int32_t, int32_t, float, float, void*);
typedef void (*CVisemeCb)(int32_t, float, void*);
typedef void (*CVoidCb)(void*);
typedef void (*CErrorCb)(const char*, void*);

namespace RustTts {

class Loader {
public:
    static Loader& Instance();
    bool Initialize();
    bool IsLoaded() const { return m_hModule != nullptr; }
    const std::string& GetLastError() const { return m_lastError; }

    // Function pointers — resolved by GetProcAddress in Initialize()
    tts_ctx*      (*create)(const char*, const char*) = nullptr;
    void          (*destroy)(tts_ctx*) = nullptr;
    int32_t       (*speak)(tts_ctx*, const char*) = nullptr;
    int32_t       (*speakSsml)(tts_ctx*, const char*) = nullptr;
    int32_t       (*speakSync)(tts_ctx*, const char*) = nullptr;
    void          (*stop)(tts_ctx*) = nullptr;
    void          (*setVoice)(tts_ctx*, const char*) = nullptr;
    void          (*setRate)(tts_ctx*, float) = nullptr;
    void          (*setPitch)(tts_ctx*, float) = nullptr;
    void          (*setVolume)(tts_ctx*, float) = nullptr;
    void          (*setOnAudio)(tts_ctx*, CAudioCb, void*) = nullptr;
    void          (*setOnBoundary2)(tts_ctx*, CBoundaryCb2, void*) = nullptr;
    void          (*setOnViseme)(tts_ctx*, CVisemeCb, void*) = nullptr;
    void          (*setOnStart)(tts_ctx*, CVoidCb, void*) = nullptr;
    void          (*setOnEnd)(tts_ctx*, CVoidCb, void*) = nullptr;
    void          (*setOnError)(tts_ctx*, CErrorCb, void*) = nullptr;
    const char*   (*getLastError)(tts_ctx*) = nullptr;

private:
    Loader();
    ~Loader();
    Loader(const Loader&) = delete;
    Loader& operator=(const Loader&) = delete;

    HMODULE m_hModule = nullptr;
    std::string m_lastError;

    template<typename T>
    bool GetFunc(const char* name, T& funcPtr);
};

} // namespace RustTts
