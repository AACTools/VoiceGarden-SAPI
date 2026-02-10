#include "SherpaOnnxEngine.h"
#include <cstring>
#include <algorithm>

namespace SherpaOnnx {

Engine::Engine(const ModelConfig& config)
    : m_config(config)
{
    // Build C API configuration
    SherpaOnnxOfflineTtsConfig apiConfig = BuildCApiConfig();

    // Create the TTS engine
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
    // Build VITS model config (must persist during SherpaOnnxCreateOfflineTts call)
    static std::string modelStr = m_config.vits.model;
    static std::string tokensStr = m_config.vits.tokens;
    static std::string dataDirStr = m_config.vits.dataDir;
    static std::string lexiconStr = m_config.vits.lexicon;
    static std::string dictDirStr = m_config.vits.dictDir;
    static std::string providerStr = m_config.provider;
    static std::string ruleFstsStr = m_config.ruleFsts;
    static std::string ruleFarsStr = m_config.ruleFars;

    // Build VITS model config
    SherpaOnnxOfflineTtsVitsModelConfig vitsConfig;
    vitsConfig.model = modelStr.c_str();
    vitsConfig.lexicon = lexiconStr.empty() ? nullptr : lexiconStr.c_str();
    vitsConfig.tokens = tokensStr.c_str();
    vitsConfig.data_dir = dataDirStr.empty() ? nullptr : dataDirStr.c_str();
    vitsConfig.dict_dir = dictDirStr.empty() ? nullptr : dictDirStr.c_str();
    vitsConfig.noise_scale = m_config.vits.noiseScale;
    vitsConfig.noise_scale_w = m_config.vits.noiseScaleW;
    vitsConfig.length_scale = m_config.vits.lengthScale;

    // Build model config
    SherpaOnnxOfflineTtsModelConfig modelConfig;
    modelConfig.vits = vitsConfig;
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
