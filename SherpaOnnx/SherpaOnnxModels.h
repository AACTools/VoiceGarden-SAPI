#pragma once

#include "SherpaOnnxConfig.h"
#include <string>
#include <vector>
#include <filesystem>

namespace SherpaOnnx {

// Information about a discovered SherpaOnnx voice model
struct VoiceInfo {
    std::string name;           // e.g., "vits-en-ljspeech"
    std::string displayName;    // e.g., "English (LJSpeech)"
    std::string language;       // e.g., "en-US"
    std::string modelPath;      // Full path to model.onnx
    std::string tokensPath;     // Full path to tokens.txt
    std::string dataDir;        // Path to espeak-ng data (optional)
    int speakerCount = 1;       // Number of speakers (0 = multi-speaker)
    int sampleRate = 22050;     // Model sample rate
};

// Model discovery and validation utilities
class Models {
public:
    // Scan directories for SherpaOnnx models
    static std::vector<VoiceInfo> DiscoverModels(
        const std::vector<std::wstring>& searchPaths);

    // Validate model files exist and are readable
    static bool ValidateModel(const VoiceInfo& info);

    // Get default model directories to search
    static std::vector<std::wstring> GetDefaultModelPaths();

    // Load voice configuration from engines_config.json
    static std::vector<VoiceInfo> LoadFromConfigJson(
        const std::wstring& configPath);

private:
    // Parse a model directory to extract voice info
    static VoiceInfo ParseModelDirectory(
        const std::filesystem::path& modelDir);

    // Check if directory has required files
    static bool HasRequiredFiles(const std::filesystem::path& dir);

    // Convert wide string to UTF-8
    static std::string WideToUTF8(const std::wstring& wstr);

    // Convert UTF-8 to wide string
    static std::wstring UTF8ToWide(const std::string& str);
};

} // namespace SherpaOnnx
