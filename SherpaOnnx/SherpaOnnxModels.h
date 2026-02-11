#pragma once

#include "SherpaOnnxConfig.h"
#include <string>
#include <vector>
#include <filesystem>

namespace SherpaOnnx {

// Model type enumeration
enum class ModelType {
    Vits,       // Standard VITS model (model.onnx + tokens.txt)
    Matcha,     // Matcha-TTS (acoustic_model + vocoder + tokens.txt)
    Kokoro,     // Kokoro (model.onnx + voices.bin + tokens.txt)
    Piper,      // Piper VITS (model.onnx + tokens.txt + espeak-ng-data)
    MMS,        // MMS (model.onnx + tokens.txt)
    Unknown     // Unable to determine
};

// Information about a discovered SherpaOnnx voice model
struct VoiceInfo {
    std::string name;           // e.g., "vits-en-ljspeech"
    std::string displayName;    // e.g., "English (LJSpeech)"
    std::string language;       // e.g., "en-US"
    ModelType modelType = ModelType::Unknown;

    // VITS/Piper/MMS paths
    std::string modelPath;      // Full path to model.onnx
    std::string tokensPath;     // Full path to tokens.txt
    std::string dataDir;        // Path to espeak-ng data (optional)

    // Matcha-specific paths
    std::string acousticModelPath;  // For Matcha: model-steps-X.onnx
    std::string vocoderPath;        // For Matcha: vocoder.onnx

    // Kokoro-specific paths
    std::string voicesPath;         // For Kokoro: voices.bin

    int speakerCount = 1;       // Number of speakers (0 = multi-speaker)
    int sampleRate = 22050;     // Model sample rate

    // Helper to check if this is a Matcha model
    bool IsMatcha() const { return modelType == ModelType::Matcha; }

    // Helper to check if this is a Kokoro model
    bool IsKokoro() const { return modelType == ModelType::Kokoro; }

    // Helper to check if this is a VITS/Piper/MMS model
    bool IsVits() const { return modelType == ModelType::Vits || modelType == ModelType::Piper || modelType == ModelType::MMS; }
};

struct ModelScanError {
    std::string modelName;
    std::string message;
};

// Model discovery and validation utilities
class Models {
public:
    // Scan directories for SherpaOnnx models
    static std::vector<VoiceInfo> DiscoverModels(
        const std::vector<std::wstring>& searchPaths);

    // Scan directories and return both valid voices and per-model validation errors.
    static std::pair<std::vector<VoiceInfo>, std::vector<ModelScanError>> DiscoverModelsWithErrors(
        const std::vector<std::wstring>& searchPaths);

    // Validate model files exist and are readable
    static bool ValidateModel(const VoiceInfo& info);
    static bool ValidateModel(const VoiceInfo& info, std::string& error);

    // Get default model directories to search
    static std::vector<std::wstring> GetDefaultModelPaths();

    // Load voice configuration from engines_config.json
    static std::vector<VoiceInfo> LoadFromConfigJson(
        const std::wstring& configPath);

private:
    // Detect model type from directory contents
    static ModelType DetectModelType(const std::filesystem::path& modelDir);

    // Parse a model directory to extract voice info
    static VoiceInfo ParseModelDirectory(
        const std::filesystem::path& modelDir);

    // Check if directory has required files
    static bool HasRequiredFiles(const std::filesystem::path& dir);

    // Convert wide string to UTF-8
    static std::string WideToUTF8(const std::wstring& wstr);

    // Convert UTF-8 to wide string
    static std::wstring UTF8ToWide(const std::string& str);

    // Find ONNX file matching a pattern
    static std::string FindOnnxFile(const std::filesystem::path& dir,
                                    const std::string& pattern);

    // Parse / normalize locale metadata from model id/name/config.
    static std::string NormalizeLocale(const std::string& locale);
    static std::string InferLocaleFromName(const std::string& name);
    static std::string BuildDisplayName(const std::string& modelName, const std::string& locale);
    static ModelType ParseModelType(const std::string& modelType);
};

} // namespace SherpaOnnx
