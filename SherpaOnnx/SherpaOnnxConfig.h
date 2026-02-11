#pragma once

#include <string>

namespace SherpaOnnx {

// Model type enumeration
enum class TtsModelType {
    Vits,       // Standard VITS model (model.onnx + tokens.txt)
    Matcha,     // Matcha-TTS (acoustic_model + vocoder + tokens.txt)
    Kokoro,     // Kokoro (model.onnx + voices.bin + tokens.txt)
    Unknown     // Unable to determine
};

// Mirrors SherpaOnnxOfflineTtsVitsModelConfig from c-api.h
struct VitsModelConfig {
    std::string model;      // Path to model.onnx
    std::string lexicon;    // Path to lexicon.txt (optional)
    std::string tokens;     // Path to tokens.txt
    std::string dataDir;    // Path to espeak-ng-data directory
    std::string dictDir;    // Path to dict directory (optional)

    float noiseScale = 0.667f;
    float noiseScaleW = 0.8f;
    float lengthScale = 1.0f;  // 1.0 = normal, <1 = faster, >1 = slower
};

// Configuration for Matcha-TTS models
struct MatchaModelConfig {
    std::string acousticModel;  // Path to model-steps-X.onnx
    std::string vocoder;        // Path to vocoder.onnx (vocos)
    std::string tokens;         // Path to tokens.txt
    std::string lexicon;        // Path to lexicon.txt (optional)
    std::string dataDir;        // Path to espeak-ng-data directory
    std::string dictDir;        // Path to dict directory (optional)

    float noiseScale = 1.0f;
    float lengthScale = 1.0f;
};

// Configuration for Kokoro models
struct KokoroModelConfig {
    std::string model;      // Path to model.onnx
    std::string voices;     // Path to voices.bin
    std::string tokens;     // Path to tokens.txt
    std::string lexicon;    // Path to lexicon.txt (optional, can be multiple files)
    std::string dataDir;    // Path to data directory
    std::string dictDir;    // Path to dict directory (optional)
    std::string lang;       // Language code for Kokoro >= 1.0 (optional)

    float lengthScale = 1.0f;
};

// Configuration for SherpaOnnx TTS engine
struct ModelConfig {
    TtsModelType modelType = TtsModelType::Vits;

    // Model-specific configs (union-style)
    VitsModelConfig vits;
    MatchaModelConfig matcha;
    KokoroModelConfig kokoro;

    int numThreads = 1;
    bool debug = false;
    std::string provider = "cpu";  // "cpu" or "cuda"
    std::string ruleFsts;          // Optional rule FSTs
    std::string ruleFars;          // Optional rule FARs
    int maxNumSentences = 2;       // Maximum number of sentences
    float silenceScale = 0.5f;     // Silence scale

    // Voice identification
    std::string voiceName;
    std::string language;
    std::string displayName;
};

} // namespace SherpaOnnx
