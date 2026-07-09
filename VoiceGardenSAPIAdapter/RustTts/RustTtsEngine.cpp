#include "pch.h"
#include "RustTtsEngine.h"
#include <spdlog/spdlog.h>

namespace RustTts {

Engine::Engine() = default;

Engine::~Engine() {
    Destroy();
}

bool Engine::Create(const std::string& engineId, const std::string& credentialsJson) {
    auto& loader = Loader::Instance();
    if (!loader.IsLoaded()) {
        spdlog::warn("RustTts::Engine::Create: tts_wrapper.dll not loaded");
        return false;
    }

    m_ctx = loader.create(engineId.c_str(),
                          credentialsJson.empty() ? nullptr : credentialsJson.c_str());
    if (!m_ctx) {
        const char* err = loader.getLastError(nullptr);
        spdlog::warn("RustTts::Engine::Create failed for '{}': {}", engineId,
                     err ? err : "(unknown)");
        return false;
    }

    RegisterCallbacks();
    spdlog::info("RustTts::Engine::Create: engine '{}' created", engineId);
    return true;
}

void Engine::Destroy() {
    if (m_ctx) {
        auto& loader = Loader::Instance();
        if (loader.IsLoaded()) {
            loader.destroy(m_ctx);
        }
        m_ctx = nullptr;
    }
}

bool Engine::Speak(const std::string& text) {
    if (!m_ctx) return false;
    auto& loader = Loader::Instance();
    int32_t rc = loader.speak(m_ctx, text.c_str());
    if (rc != 0) {
        const char* err = loader.getLastError(m_ctx);
        spdlog::warn("RustTts::Engine::Speak failed: {}", err ? err : "(unknown)");
    }
    return rc == 0;
}

bool Engine::SpeakSsml(const std::string& ssml) {
    if (!m_ctx) return false;
    auto& loader = Loader::Instance();
    int32_t rc = loader.speakSsml(m_ctx, ssml.c_str());
    if (rc != 0) {
        const char* err = loader.getLastError(m_ctx);
        spdlog::warn("RustTts::Engine::SpeakSsml failed: {}", err ? err : "(unknown)");
    }
    return rc == 0;
}

void Engine::Stop() {
    if (!m_ctx) return;
    auto& loader = Loader::Instance();
    loader.stop(m_ctx);
}

void Engine::SetVoice(const std::string& voiceId) {
    if (!m_ctx) return;
    Loader::Instance().setVoice(m_ctx, voiceId.c_str());
}

void Engine::SetRate(float rate) {
    if (!m_ctx) return;
    Loader::Instance().setRate(m_ctx, rate);
}

void Engine::SetPitch(float pitch) {
    if (!m_ctx) return;
    Loader::Instance().setPitch(m_ctx, pitch);
}

void Engine::SetVolume(float volume) {
    if (!m_ctx) return;
    Loader::Instance().setVolume(m_ctx, volume);
}

void Engine::SetOnAudio(AudioCallback cb) {
    m_onAudio = std::move(cb);
}

void Engine::SetOnBoundary(BoundaryCallback cb) {
    m_onBoundary = std::move(cb);
}

void Engine::SetOnViseme(VisemeCallback cb) {
    m_onViseme = std::move(cb);
}

void Engine::SetOnError(ErrorCallback cb) {
    m_onError = std::move(cb);
}

std::string Engine::GetLastError() const {
    if (!m_ctx) return {};
    const char* err = Loader::Instance().getLastError(m_ctx);
    return err ? std::string(err) : std::string{};
}

void Engine::RegisterCallbacks() {
    auto& loader = Loader::Instance();
    loader.setOnAudio(m_ctx, &Engine::OnAudioThunk, this);
    loader.setOnBoundary2(m_ctx, &Engine::OnBoundaryThunk, this);
    loader.setOnViseme(m_ctx, &Engine::OnVisemeThunk, this);
    loader.setOnError(m_ctx, &Engine::OnErrorThunk, this);
}

// Static thunks — called by the Rust side during synthesis.
// The userdata pointer is `this`, set in RegisterCallbacks().

void Engine::OnAudioThunk(const uint8_t* data, uintptr_t len, void* ud) {
    auto* self = static_cast<Engine*>(ud);
    if (self && self->m_onAudio) {
        self->m_onAudio(data, static_cast<uint32_t>(len));
    }
}

void Engine::OnBoundaryThunk(const char* word, int32_t charOffset,
                              int32_t charLen, float startS, float endS,
                              void* ud) {
    auto* self = static_cast<Engine*>(ud);
    if (self && self->m_onBoundary) {
        self->m_onBoundary(word, charOffset, charLen, startS, endS);
    }
}

void Engine::OnVisemeThunk(int32_t visemeId, float offsetS, void* ud) {
    auto* self = static_cast<Engine*>(ud);
    if (self && self->m_onViseme) {
        self->m_onViseme(visemeId, offsetS);
    }
}

void Engine::OnErrorThunk(const char* msg, void* ud) {
    auto* self = static_cast<Engine*>(ud);
    if (self && self->m_onError) {
        self->m_onError(msg);
    }
}

} // namespace RustTts
