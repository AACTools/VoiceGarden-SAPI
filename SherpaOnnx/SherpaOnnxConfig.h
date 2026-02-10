#pragma once

#include <string>

namespace SherpaOnnx {

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

// Configuration for SherpaOnnx TTS engine
struct ModelConfig {
    VitsModelConfig vits;
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
