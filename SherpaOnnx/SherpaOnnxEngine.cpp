#include "SherpaOnnxEngine.h"
#include <cstring>
#include <algorithm>

namespace SherpaOnnx {

Engine::Engine(const ModelConfig& config)
    : m_config(config)
{
    // Initialize dynamic loader first
    if (!Dynamic::Loader().Initialize()) {
        m_lastError = "Failed to initialize SherpaOnnx DLL: ";
        m_lastError += Dynamic::Loader().GetLastError();
        return;
    }

    // Build C API configuration
    SherpaOnnxOfflineTtsConfig apiConfig = BuildCApiConfig();

    // Create the TTS engine (uses dynamic loading)
    m_tts = SherpaOnnxCreateOfflineTts(&apiConfig);

    if (!m_tts) {
        m_lastError = "Failed to create SherpaOnnx TTS engine";
    }

    // Free the temporary C strings used in config
    // (SherpaOnnx should make its own copies)
}

Engine::~Engine()
{
    if (m_tts) {
        SherpaOnnxDestroyOfflineTts(m_tts);
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
        SherpaOnnxOfflineTtsGenerate(m_tts, text.c_str(), 0, speed);

    if (!audio) {
        m_lastError = "Failed to generate audio";
        return {};
    }

    // Convert to vector
    std::vector<float> result = ConvertGeneratedAudio(audio);

    // Clean up
    SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);

    return result;
}

int Engine::GetSampleRate() const
{
    if (!m_tts) {
        return 0;
    }
    return SherpaOnnxOfflineTtsSampleRate(m_tts);
}

int Engine::GetNumSpeakers() const
{
    if (!m_tts) {
        return 0;
    }
    return SherpaOnnxOfflineTtsNumSpeakers(m_tts);
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
    // Static strings to persist during SherpaOnnxCreateOfflineTts call
    static std::string providerStr = m_config.provider;
    static std::string ruleFstsStr = m_config.ruleFsts;
    static std::string ruleFarsStr = m_config.ruleFars;

    // Build model config based on model type
    SherpaOnnxOfflineTtsModelConfig modelConfig = {};

    switch (m_config.modelType) {
        case TtsModelType::Matcha: {
            // Matcha-TTS configuration
            static std::string acousticModelStr = m_config.matcha.acousticModel;
            static std::string vocoderStr = m_config.matcha.vocoder;
            static std::string tokensStr = m_config.matcha.tokens;
            static std::string lexiconStr = m_config.matcha.lexicon;
            static std::string dataDirStr = m_config.matcha.dataDir;
            static std::string dictDirStr = m_config.matcha.dictDir;

            SherpaOnnxOfflineTtsMatchaModelConfig matchaConfig;
            matchaConfig.acoustic_model = acousticModelStr.c_str();
            matchaConfig.vocoder = vocoderStr.c_str();
            matchaConfig.tokens = tokensStr.c_str();
            matchaConfig.lexicon = lexiconStr.empty() ? nullptr : lexiconStr.c_str();
            matchaConfig.data_dir = dataDirStr.empty() ? nullptr : dataDirStr.c_str();
            matchaConfig.dict_dir = dictDirStr.empty() ? nullptr : dictDirStr.c_str();
            matchaConfig.noise_scale = m_config.matcha.noiseScale;
            matchaConfig.length_scale = m_config.matcha.lengthScale;

            modelConfig.matcha = matchaConfig;
            break;
        }

        case TtsModelType::Kokoro: {
            // Kokoro configuration
            static std::string modelStr = m_config.kokoro.model;
            static std::string voicesStr = m_config.kokoro.voices;
            static std::string tokensStr = m_config.kokoro.tokens;
            static std::string lexiconStr = m_config.kokoro.lexicon;
            static std::string dataDirStr = m_config.kokoro.dataDir;
            static std::string dictDirStr = m_config.kokoro.dictDir;
            static std::string langStr = m_config.kokoro.lang;

            SherpaOnnxOfflineTtsKokoroModelConfig kokoroConfig;
            kokoroConfig.model = modelStr.c_str();
            kokoroConfig.voices = voicesStr.c_str();
            kokoroConfig.tokens = tokensStr.c_str();
            kokoroConfig.lexicon = lexiconStr.empty() ? nullptr : lexiconStr.c_str();
            kokoroConfig.data_dir = dataDirStr.empty() ? nullptr : dataDirStr.c_str();
            kokoroConfig.dict_dir = dictDirStr.empty() ? nullptr : dictDirStr.c_str();
            kokoroConfig.lang = langStr.empty() ? nullptr : langStr.c_str();
            kokoroConfig.length_scale = m_config.kokoro.lengthScale;

            modelConfig.kokoro = kokoroConfig;
            break;
        }

        case TtsModelType::Vits:
        default: {
            // VITS/Piper/MMS configuration
            static std::string modelStr = m_config.vits.model;
            static std::string tokensStr = m_config.vits.tokens;
            static std::string dataDirStr = m_config.vits.dataDir;
            static std::string lexiconStr = m_config.vits.lexicon;
            static std::string dictDirStr = m_config.vits.dictDir;

            SherpaOnnxOfflineTtsVitsModelConfig vitsConfig;
            vitsConfig.model = modelStr.c_str();
            vitsConfig.lexicon = lexiconStr.empty() ? nullptr : lexiconStr.c_str();
            vitsConfig.tokens = tokensStr.c_str();
            vitsConfig.data_dir = dataDirStr.empty() ? nullptr : dataDirStr.c_str();
            vitsConfig.dict_dir = dictDirStr.empty() ? nullptr : dictDirStr.c_str();
            vitsConfig.noise_scale = m_config.vits.noiseScale;
            vitsConfig.noise_scale_w = m_config.vits.noiseScaleW;
            vitsConfig.length_scale = m_config.vits.lengthScale;

            modelConfig.vits = vitsConfig;
            break;
        }
    }

    modelConfig.num_threads = m_config.numThreads;
    modelConfig.debug = m_config.debug ? 1 : 0;
    modelConfig.provider = providerStr.c_str();

    // Build main config
    SherpaOnnxOfflineTtsConfig config;
    config.model = modelConfig;
    config.rule_fsts = ruleFstsStr.empty() ? nullptr : ruleFstsStr.c_str();
    config.max_num_sentences = m_config.maxNumSentences;
    config.rule_fars = ruleFarsStr.empty() ? nullptr : ruleFarsStr.c_str();
    config.silence_scale = m_config.silenceScale;

    return config;
}

} // namespace SherpaOnnx
