#pragma once

// RAII wrapper for a rust-tts-wrapper engine instance (tts_ctx).
// Owns the context lifetime and marshals callbacks back to C++ lambdas.

#include <string>
#include <cstdint>
#include <functional>
#include <memory>
#include "RustTtsLoader.h"

namespace RustTts {

// Audio chunk callback: (pcmBytes, numBytes)
using AudioCallback = std::function<void(const uint8_t*, uint32_t)>;

// Boundary callback: (word, charOffset, charLen, startSec, endSec)
using BoundaryCallback = std::function<void(const char*, int32_t, int32_t, float, float)>;

// Viseme callback: (visemeId, offsetSec)
using VisemeCallback = std::function<void(int32_t, float)>;

class Engine {
public:
    Engine();
    ~Engine();

    Engine(const Engine&) = delete;
    Engine& operator=(const Engine&) = delete;

    // Create an engine instance. Returns true on success.
    // engineId: "openai", "google", "elevenlabs", "azure", "edge", "cartesia", etc.
    // credentialsJson: JSON string with API keys, or empty for credential-free engines.
    bool Create(const std::string& engineId, const std::string& credentialsJson);

    // True if the engine context is valid.
    bool IsValid() const { return m_ctx != nullptr; }

    // Destroy the engine context.
    void Destroy();

    // Speak plain text. Returns true on success.
    bool Speak(const std::string& text);

    // Speak pre-built SSML. Returns true on success.
    bool SpeakSsml(const std::string& ssml);

    // Stop in-progress speech.
    void Stop();

    // Set voice, rate (1.0=normal), pitch (1.0=normal), volume (1.0=normal).
    void SetVoice(const std::string& voiceId);
    void SetRate(float rate);
    void SetPitch(float pitch);
    void SetVolume(float volume);

    // Register callbacks. The engine stores these and calls them during Speak().
    void SetOnAudio(AudioCallback cb);
    void SetOnBoundary(BoundaryCallback cb);
    void SetOnViseme(VisemeCallback cb);

    // Get the last error message from the Rust side.
    std::string GetLastError() const;

private:
    tts_ctx* m_ctx = nullptr;

    // Callbacks — stored as members so they live as long as the engine.
    AudioCallback m_onAudio;
    BoundaryCallback m_onBoundary;
    VisemeCallback m_onViseme;

    // Register the static thunk callbacks with the Rust side.
    void RegisterCallbacks();

    // Static thunks that route to the instance via userdata.
    static void OnAudioThunk(const uint8_t* data, uintptr_t len, void* ud);
    static void OnBoundaryThunk(const char* word, int32_t charOffset,
                                int32_t charLen, float startS, float endS,
                                void* ud);
    static void OnVisemeThunk(int32_t visemeId, float offsetS, void* ud);
};

} // namespace RustTts
