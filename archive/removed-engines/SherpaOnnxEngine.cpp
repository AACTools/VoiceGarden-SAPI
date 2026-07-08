#include "SherpaOnnxEngine.h"
#include <cstring>
#include <algorithm>
#include <filesystem>

namespace SherpaOnnx {

namespace {
struct ProgressCallbackContext {
    const std::function<bool(const float* samples, int32_t n, float progress)>* callback = nullptr;
};

int32_t InvokeProgressCallback(const float* samples, int32_t n, float p, void* arg) {
    auto* ctx = reinterpret_cast<ProgressCallbackContext*>(arg);
    if (!ctx || !ctx->callback) {
        return 0;
    }
    return (*ctx->callback)(samples, n, p) ? 1 : 0;
}

bool IsExistingFile(const std::string& path) {
    if (path.empty()) {
        return false;
    }
    std::error_code ec;
    return std::filesystem::is_regular_file(std::filesystem::u8path(path), ec);
}

bool IsExistingDirectory(const std::string& path) {
    if (path.empty()) {
        return false;
    }
    std::error_code ec;
    return std::filesystem::is_directory(std::filesystem::u8path(path), ec);
}
}

Engine::Engine(const ModelConfig& config)
    : m_config(config)
{
    std::string validationError;
    if (!ValidateConfig(validationError)) {
        m_lastError = "Invalid Sherpa config: " + validationError;
        return;
    }

    // Initialize dynamic loader first
    if (!Dynamic::Loader().Initialize()) {
        m_lastError = "Failed to initialize SherpaOnnx DLL: ";
        m_lastError += Dynamic::Loader().GetLastError();
        return;
    }

    // Build C API configuration
    SherpaOnnxOfflineTtsConfig apiConfig = {};
    if (m_config.modelType == TtsModelType::Vits) {
        // Baseline parity path: match known-good vanilla C usage.
        m_ownedStrings.clear();
        m_ownedStrings.reserve(12);
        apiConfig.model.vits.model = PersistString(m_config.vits.model);
        apiConfig.model.vits.tokens = PersistString(m_config.vits.tokens);
        apiConfig.model.vits.data_dir = PersistString(m_config.vits.dataDir);
        apiConfig.model.num_threads = (std::max)(1, m_config.numThreads);
        apiConfig.model.provider = PersistString(m_config.provider.empty() ? "cpu" : m_config.provider);
        apiConfig.max_num_sentences = (std::max)(1, m_config.maxNumSentences);
    } else {
        apiConfig = BuildCApiConfig();
    }

    // Create the TTS engine (uses dynamic loading)
    m_tts = Dynamic::Loader().SherpaOnnxCreateOfflineTts(&apiConfig);

    // Some Piper/VITS packs can fail init when data_dir is present but incompatible.
    // Retry once without data_dir to provide a robust baseline path.
    if (!m_tts && m_config.modelType == TtsModelType::Vits && !m_config.vits.dataDir.empty()) {
        SherpaOnnxOfflineTtsConfig retryConfig = apiConfig;
        retryConfig.model.vits.data_dir = nullptr;
        m_tts = Dynamic::Loader().SherpaOnnxCreateOfflineTts(&retryConfig);
        if (m_tts) {
            m_lastError.clear();
            return;
        }
    }

    if (!m_tts) {
        m_lastError = "Failed to create SherpaOnnx TTS engine";
    }

    // Free the temporary C strings used in config
    // (SherpaOnnx should make its own copies)
}

Engine::~Engine()
{
    if (m_tts) {
        Dynamic::Loader().SherpaOnnxDestroyOfflineTts(m_tts);
        m_tts = nullptr;
    }
}

std::vector<float> Engine::Generate(const std::string& text, float speed)
{
    if (!m_tts) {
        m_lastError = "Engine not initialized";
        return {};
    }

    if (text.empty()) {
        return {};
    }

    // Generate speech (speaker 0, default speed)
    const SherpaOnnxGeneratedAudio* audio =
        Dynamic::Loader().SherpaOnnxOfflineTtsGenerate(m_tts, text.c_str(), 0, speed);

    if (!audio) {
        m_lastError = "Failed to generate audio";
        return {};
    }

    // Convert to vector
    std::vector<float> result = ConvertGeneratedAudio(audio);

    // Clean up
    Dynamic::Loader().SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);

    return result;
}

bool Engine::ValidateConfig(std::string& error) const
{
    auto requireFile = [&error](const char* field, const std::string& value) -> bool {
        if (value.empty()) {
            error = std::string(field) + " is required";
            return false;
        }
        if (!IsExistingFile(value)) {
            error = std::string(field) + " does not exist: " + value;
            return false;
        }
        return true;
    };

    auto optionalDir = [&error](const char* field, const std::string& value) -> bool {
        if (value.empty()) {
            return true;
        }
        if (!IsExistingDirectory(value)) {
            error = std::string(field) + " is not a directory: " + value;
            return false;
        }
        return true;
    };

    auto optionalFileOrDir = [&error](const char* field, const std::string& value) -> bool {
        if (value.empty()) {
            return true;
        }
        if (!IsExistingFile(value) && !IsExistingDirectory(value)) {
            error = std::string(field) + " does not exist: " + value;
            return false;
        }
        return true;
    };

    switch (m_config.modelType) {
    case TtsModelType::Matcha:
        if (!requireFile("matcha.acousticModel", m_config.matcha.acousticModel)) return false;
        if (!requireFile("matcha.vocoder", m_config.matcha.vocoder)) return false;
        if (!requireFile("matcha.tokens", m_config.matcha.tokens)) return false;
        if (!optionalFileOrDir("matcha.lexicon", m_config.matcha.lexicon)) return false;
        if (!optionalDir("matcha.dataDir", m_config.matcha.dataDir)) return false;
        if (!optionalDir("matcha.dictDir", m_config.matcha.dictDir)) return false;
        break;
    case TtsModelType::Kokoro:
        if (!requireFile("kokoro.model", m_config.kokoro.model)) return false;
        if (!requireFile("kokoro.voices", m_config.kokoro.voices)) return false;
        if (!requireFile("kokoro.tokens", m_config.kokoro.tokens)) return false;
        if (!optionalFileOrDir("kokoro.lexicon", m_config.kokoro.lexicon)) return false;
        if (!optionalDir("kokoro.dataDir", m_config.kokoro.dataDir)) return false;
        if (!optionalDir("kokoro.dictDir", m_config.kokoro.dictDir)) return false;
        break;
    case TtsModelType::Vits:
    default:
        if (!requireFile("vits.model", m_config.vits.model)) return false;
        if (!requireFile("vits.tokens", m_config.vits.tokens)) return false;
        if (!optionalFileOrDir("vits.lexicon", m_config.vits.lexicon)) return false;
        if (!optionalDir("vits.dataDir", m_config.vits.dataDir)) return false;
        if (!optionalDir("vits.dictDir", m_config.vits.dictDir)) return false;
        break;
    }

    if (m_config.provider.empty()) {
        error = "provider is required";
        return false;
    }
    if (m_config.numThreads <= 0) {
        error = "numThreads must be > 0";
        return false;
    }

    return true;
}

bool Engine::GenerateWithProgressCallback(
    const std::string& text,
    float speed,
    const std::function<bool(const float* samples, int32_t n, float progress)>& onChunk)
{
    if (!m_tts) {
        m_lastError = "Engine not initialized";
        return false;
    }

    if (text.empty()) {
        return true;
    }

    if (!Dynamic::Loader().SherpaOnnxOfflineTtsGenerateWithProgressCallbackWithArg) {
        std::vector<float> fallback = Generate(text, speed);
        if (fallback.empty()) {
            return false;
        }
        return onChunk(fallback.data(), static_cast<int32_t>(fallback.size()), 1.0f);
    }

    ProgressCallbackContext ctx{ &onChunk };
    const SherpaOnnxGeneratedAudio* audio =
        Dynamic::Loader().SherpaOnnxOfflineTtsGenerateWithProgressCallbackWithArg(
            m_tts,
            text.c_str(),
            0,
            speed,
            InvokeProgressCallback,
            &ctx);

    if (!audio) {
        m_lastError = "Failed to generate audio with callback";
        return false;
    }

    Dynamic::Loader().SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
    return true;
}

int Engine::GetSampleRate() const
{
    if (!m_tts) {
        return 0;
    }
    return Dynamic::Loader().SherpaOnnxOfflineTtsSampleRate(m_tts);
}

int Engine::GetNumSpeakers() const
{
    if (!m_tts) {
        return 0;
    }
    return Dynamic::Loader().SherpaOnnxOfflineTtsNumSpeakers(m_tts);
}

std::vector<float> Engine::ConvertGeneratedAudio(
    const SherpaOnnxGeneratedAudio* audio)
{
    if (!audio) {
        return {};
    }

    // SherpaOnnxGeneratedAudio has direct fields:
    // const float *samples;  // in the range [-1, 1]
    // int32_t n;             // number of samples
    // int32_t sample_rate;

    const float* data = audio->samples;
    int32_t numSamples = audio->n;

    if (!data || numSamples <= 0) {
        return {};
    }

    return std::vector<float>(data, data + numSamples);
}

SherpaOnnxOfflineTtsConfig Engine::BuildCApiConfig()
{
    m_ownedStrings.clear();
    m_ownedStrings.reserve(48);

    // Build model config based on model type
    SherpaOnnxOfflineTtsModelConfig modelConfig = {};
    modelConfig.vits.model = PersistString("");
    modelConfig.vits.lexicon = PersistString("");
    modelConfig.vits.tokens = PersistString("");
    modelConfig.vits.data_dir = PersistString("");
    modelConfig.vits.noise_scale = 0.667f;
    modelConfig.vits.noise_scale_w = 0.8f;
    modelConfig.vits.length_scale = 1.0f;
    modelConfig.vits.dict_dir = PersistString("");

    modelConfig.matcha.acoustic_model = PersistString("");
    modelConfig.matcha.vocoder = PersistString("");
    modelConfig.matcha.lexicon = PersistString("");
    modelConfig.matcha.tokens = PersistString("");
    modelConfig.matcha.data_dir = PersistString("");
    modelConfig.matcha.noise_scale = 0.667f;
    modelConfig.matcha.length_scale = 1.0f;
    modelConfig.matcha.dict_dir = PersistString("");

    modelConfig.kokoro.model = PersistString("");
    modelConfig.kokoro.voices = PersistString("");
    modelConfig.kokoro.tokens = PersistString("");
    modelConfig.kokoro.data_dir = PersistString("");
    modelConfig.kokoro.length_scale = 1.0f;
    modelConfig.kokoro.dict_dir = PersistString("");
    modelConfig.kokoro.lexicon = PersistString("");
    modelConfig.kokoro.lang = PersistString("");

    modelConfig.kitten.model = PersistString("");
    modelConfig.kitten.voices = PersistString("");
    modelConfig.kitten.tokens = PersistString("");
    modelConfig.kitten.data_dir = PersistString("");
    modelConfig.kitten.length_scale = 1.0f;

    modelConfig.zipvoice.tokens = PersistString("");
    modelConfig.zipvoice.encoder = PersistString("");
    modelConfig.zipvoice.decoder = PersistString("");
    modelConfig.zipvoice.vocoder = PersistString("");
    modelConfig.zipvoice.data_dir = PersistString("");
    modelConfig.zipvoice.lexicon = PersistString("");
    modelConfig.zipvoice.feat_scale = 0.1f;
    modelConfig.zipvoice.t_shift = 0.5f;
    modelConfig.zipvoice.target_rms = 0.1f;
    modelConfig.zipvoice.guidance_scale = 1.0f;

    switch (m_config.modelType) {
        case TtsModelType::Matcha: {
            // Matcha-TTS configuration
            SherpaOnnxOfflineTtsMatchaModelConfig matchaConfig;
            matchaConfig.acoustic_model = PersistString(m_config.matcha.acousticModel);
            matchaConfig.vocoder = PersistString(m_config.matcha.vocoder);
            matchaConfig.tokens = PersistString(m_config.matcha.tokens);
            matchaConfig.lexicon = PersistString(m_config.matcha.lexicon);
            matchaConfig.data_dir = PersistString(m_config.matcha.dataDir);
            matchaConfig.dict_dir = PersistString(m_config.matcha.dictDir);
            matchaConfig.noise_scale = m_config.matcha.noiseScale;
            matchaConfig.length_scale = m_config.matcha.lengthScale;

            modelConfig.matcha = matchaConfig;
            break;
        }

        case TtsModelType::Kokoro: {
            // Kokoro configuration
            SherpaOnnxOfflineTtsKokoroModelConfig kokoroConfig;
            kokoroConfig.model = PersistString(m_config.kokoro.model);
            kokoroConfig.voices = PersistString(m_config.kokoro.voices);
            kokoroConfig.tokens = PersistString(m_config.kokoro.tokens);
            kokoroConfig.lexicon = PersistString(m_config.kokoro.lexicon);
            kokoroConfig.data_dir = PersistString(m_config.kokoro.dataDir);
            kokoroConfig.dict_dir = PersistString(m_config.kokoro.dictDir);
            std::string kokoroLang = m_config.kokoro.lang.empty() ? "en-us" : m_config.kokoro.lang;
            kokoroConfig.lang = PersistString(kokoroLang);
            kokoroConfig.length_scale = m_config.kokoro.lengthScale;

            modelConfig.kokoro = kokoroConfig;
            break;
        }

        case TtsModelType::Vits:
        default: {
            // VITS/Piper/MMS configuration
            SherpaOnnxOfflineTtsVitsModelConfig vitsConfig;
            vitsConfig.model = PersistString(m_config.vits.model);
            vitsConfig.lexicon = PersistString(m_config.vits.lexicon);
            vitsConfig.tokens = PersistString(m_config.vits.tokens);
            vitsConfig.data_dir = PersistString(m_config.vits.dataDir);
            vitsConfig.dict_dir = PersistString(m_config.vits.dictDir);
            vitsConfig.noise_scale = m_config.vits.noiseScale;
            vitsConfig.noise_scale_w = m_config.vits.noiseScaleW;
            vitsConfig.length_scale = m_config.vits.lengthScale;

            modelConfig.vits = vitsConfig;
            break;
        }
    }

    modelConfig.num_threads = m_config.numThreads;
    modelConfig.debug = m_config.debug ? 1 : 0;
    modelConfig.provider = PersistString(m_config.provider);

    // Build main config
    SherpaOnnxOfflineTtsConfig config;
    config.model = modelConfig;
    config.rule_fsts = PersistString(m_config.ruleFsts);
    config.max_num_sentences = m_config.maxNumSentences;
    config.rule_fars = PersistString(m_config.ruleFars);
    config.silence_scale = m_config.silenceScale;

    return config;
}

const char* Engine::PersistString(const std::string& value, bool nullIfEmpty)
{
    if (nullIfEmpty && value.empty()) {
        return nullptr;
    }

    m_ownedStrings.push_back(value);
    return m_ownedStrings.back().c_str();
}

} // namespace SherpaOnnx

