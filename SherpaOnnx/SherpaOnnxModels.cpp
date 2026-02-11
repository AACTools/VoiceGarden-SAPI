#include "SherpaOnnxModels.h"
#include <fstream>
#include <sstream>
#include <windows.h>
#include <shlobj.h>
#include <nlohmann/json.hpp>

// External symbol for the DLL base address
extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace SherpaOnnx {

std::vector<VoiceInfo> Models::DiscoverModels(
    const std::vector<std::wstring>& searchPaths)
{
    std::vector<VoiceInfo> voices;

    // First, try loading from engines_config.json
    wchar_t localAppPath[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA,
                                   nullptr, 0, localAppPath))) {
        std::wstring configPath =
            std::wstring(localAppPath) + L"\\OpenSpeech\\engines_config.json";

        if (std::filesystem::exists(configPath)) {
            voices = LoadFromConfigJson(configPath);
            if (!voices.empty()) {
                return voices;
            }
        }
    }

    // Fallback to directory scanning
    for (const auto& searchPath : searchPaths) {
        if (!std::filesystem::exists(searchPath)) {
            continue;
        }

        try {
            // Scan for subdirectories that might contain models
            for (const auto& entry :
                 std::filesystem::directory_iterator(searchPath)) {
                if (entry.is_directory()) {
                    VoiceInfo info = ParseModelDirectory(entry.path());
                    if (ValidateModel(info)) {
                        voices.push_back(info);
                    }
                }
            }
        } catch (const std::exception&) {
            // Skip directories we can't access
            continue;
        }
    }

    return voices;
}

bool Models::ValidateModel(const VoiceInfo& info)
{
    // Validate based on model type
    switch (info.modelType) {
        case ModelType::Matcha:
            // Matcha needs: acoustic_model, vocoder, tokens
            if (!std::filesystem::exists(info.acousticModelPath)) return false;
            if (!std::filesystem::exists(info.vocoderPath)) return false;
            if (!std::filesystem::exists(info.tokensPath)) return false;
            // Check file sizes
            try {
                if (std::filesystem::file_size(info.acousticModelPath) < 1024 * 1024) return false;
                if (std::filesystem::file_size(info.vocoderPath) < 1024 * 1024) return false;
            } catch (...) {
                return false;
            }
            return true;

        case ModelType::Kokoro:
            // Kokoro needs: model, voices, tokens
            if (!std::filesystem::exists(info.modelPath)) return false;
            if (!std::filesystem::exists(info.voicesPath)) return false;
            if (!std::filesystem::exists(info.tokensPath)) return false;
            // Check file sizes
            try {
                if (std::filesystem::file_size(info.modelPath) < 1024 * 1024) return false;
                if (std::filesystem::file_size(info.voicesPath) < 1024 * 1024) return false;
            } catch (...) {
                return false;
            }
            return true;

        case ModelType::Vits:
        case ModelType::Piper:
        case ModelType::MMS:
        default:
            // VITS-style models need: model, tokens
            if (!std::filesystem::exists(info.modelPath)) {
                return false;
            }
            if (!std::filesystem::exists(info.tokensPath)) {
                return false;
            }
            // Check file sizes (model should be substantial)
            try {
                auto modelSize = std::filesystem::file_size(info.modelPath);
                if (modelSize < 1024 * 1024) {  // Less than 1MB seems suspicious
                    return false;
                }
            } catch (...) {
                return false;
            }
            return true;
    }
}

std::vector<std::wstring> Models::GetDefaultModelPaths()
{
    std::vector<std::wstring> paths;

    // Add user-specific model directory
    wchar_t localAppPath[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA,
                                   nullptr, 0, localAppPath))) {
        paths.push_back(std::wstring(localAppPath) + L"\\OpenSpeech\\models");
    }

    // Add program data directory
    wchar_t programDataPath[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA,
                                   nullptr, 0, programDataPath))) {
        paths.push_back(std::wstring(programDataPath) +
                        L"\\OpenSpeech\\models");
    }

    // Add module directory (where DLL is located)
    wchar_t modulePath[MAX_PATH];
    if (GetModuleFileNameW((HMODULE)&__ImageBase, modulePath, MAX_PATH)) {
        std::wstring moduleDir(modulePath);
        size_t lastSlash = moduleDir.find_last_of(L"\\/");
        if (lastSlash != std::wstring::npos) {
            moduleDir = moduleDir.substr(0, lastSlash);
            paths.push_back(moduleDir + L"\\models");
            paths.push_back(moduleDir + L"\\..\\models");
        }
    }

    return paths;
}

std::vector<VoiceInfo> Models::LoadFromConfigJson(
    const std::wstring& configPath)
{
    std::vector<VoiceInfo> voices;

    try {
        std::ifstream configFile(configPath);
        if (!configFile.is_open()) {
            return voices;
        }

        // Read JSON content
        std::stringstream buffer;
        buffer << configFile.rdbuf();
        configFile.close();

        // Parse JSON
        nlohmann::json config = nlohmann::json::parse(buffer.str());

        if (!config.contains("engines")) {
            return voices;
        }

        // Iterate through engines
        for (auto& [key, engine] : config["engines"].items()) {
            std::string type = engine.value("type", "");
            if (type != "sherpa-onnx" && type != "sherpaonnx") {
                continue;
            }

            if (!engine.contains("config")) {
                continue;
            }

            auto& c = engine["config"];

            VoiceInfo info;
            info.name = c.value("voiceName", key);
            info.displayName = c.value("displayName", info.name);
            info.language = c.value("language", "en-US");
            info.modelPath = c.value("modelPath", "");
            info.tokensPath = c.value("tokensPath", "");
            info.dataDir = c.value("dataDir", "");
            info.speakerCount = c.value("speakers", 1);
            info.sampleRate = c.value("sampleRate", 22050);

            if (ValidateModel(info)) {
                voices.push_back(info);
            }
        }
    } catch (const std::exception&) {
        // JSON parse error, return empty list
    }

    return voices;
}

VoiceInfo Models::ParseModelDirectory(const std::filesystem::path& modelDir)
{
    VoiceInfo info;

    // Use directory name as voice name
    info.name = WideToUTF8(modelDir.filename().wstring());
    info.displayName = info.name;
    info.language = "en-US";  // Default, should be overridden

    // Detect model type
    info.modelType = DetectModelType(modelDir);

    // Set paths based on model type
    switch (info.modelType) {
        case ModelType::Matcha: {
            // Matcha: model-steps-X.onnx + vocoder.onnx + tokens.txt
            info.acousticModelPath = FindOnnxFile(modelDir, "model-steps");
            info.vocoderPath = FindOnnxFile(modelDir, "vocos") + info.acousticModelPath;
            if (info.vocoderPath.empty()) {
                info.vocoderPath = FindOnnxFile(modelDir, "vocoder");
            }
            info.tokensPath = WideToUTF8((modelDir / "tokens.txt").wstring());

            // Check for espeak-ng-data directory
            std::filesystem::path dataDir = modelDir / "espeak-ng-data";
            if (std::filesystem::exists(dataDir)) {
                info.dataDir = WideToUTF8(dataDir.wstring());
            }
            break;
        }

        case ModelType::Kokoro: {
            // Kokoro: model.onnx + voices.bin + tokens.txt
            info.modelPath = WideToUTF8((modelDir / "model.onnx").wstring());
            info.voicesPath = WideToUTF8((modelDir / "voices.bin").wstring());
            info.tokensPath = WideToUTF8((modelDir / "tokens.txt").wstring());
            break;
        }

        case ModelType::Vits:
        case ModelType::Piper:
        case ModelType::MMS:
        default: {
            // VITS-style: model.onnx + tokens.txt
            info.modelPath = WideToUTF8((modelDir / "model.onnx").wstring());
            info.tokensPath = WideToUTF8((modelDir / "tokens.txt").wstring());

            // Check for espeak-ng-data directory (Piper models)
            std::filesystem::path dataDir = modelDir / "espeak-ng-data";
            if (std::filesystem::exists(dataDir)) {
                info.dataDir = WideToUTF8(dataDir.wstring());
                if (info.modelType == ModelType::Unknown) {
                    info.modelType = ModelType::Piper;
                }
            }
            break;
        }
    }

    // Try to infer language from directory name
    if (info.name.find("en-") != std::string::npos ||
        info.name.find("-en-") != std::string::npos) {
        info.language = "en-US";
    } else if (info.name.find("zh-") != std::string::npos ||
               info.name.find("-zh-") != std::string::npos) {
        info.language = "zh-CN";
    } else if (info.name.find("es-") != std::string::npos ||
               info.name.find("-es-") != std::string::npos) {
        info.language = "es-ES";
    } else if (info.name.find("fr-") != std::string::npos ||
               info.name.find("-fr-") != std::string::npos) {
        info.language = "fr-FR";
    } else if (info.name.find("de-") != std::string::npos ||
               info.name.find("-de-") != std::string::npos) {
        info.language = "de-DE";
    } else if (info.name.find("ar-") != std::string::npos ||
               info.name.find("-ar-") != std::string::npos) {
        info.language = "ar-SA";
    } else if (info.name.find("ja-") != std::string::npos ||
               info.name.find("-ja-") != std::string::npos) {
        info.language = "ja-JP";
    } else if (info.name.find("ko-") != std::string::npos ||
               info.name.find("-ko-") != std::string::npos) {
        info.language = "ko-KR";
    }

    return info;
}

ModelType Models::DetectModelType(const std::filesystem::path& modelDir)
{
    // Check for Kokoro: model.onnx + voices.bin
    if (std::filesystem::exists(modelDir / "model.onnx") &&
        std::filesystem::exists(modelDir / "voices.bin")) {
        return ModelType::Kokoro;
    }

    // Check for Matcha: model-steps-X.onnx + vocoder.onnx
    bool hasModelSteps = false;
    bool hasVocoder = false;
    for (const auto& entry : std::filesystem::directory_iterator(modelDir)) {
        if (entry.is_regular_file()) {
            std::string filename = entry.path().filename().string();
            if (filename.find("model-steps") == 0 && filename.find(".onnx") != std::string::npos) {
                hasModelSteps = true;
            }
            if (filename.find("vocos") == 0 || filename.find("vocoder") == 0) {
                if (filename.find(".onnx") != std::string::npos) {
                    hasVocoder = true;
                }
            }
        }
    }
    if (hasModelSteps && hasVocoder) {
        return ModelType::Matcha;
    }

    // Check for VITS/Piper/MMS: model.onnx + tokens.txt
    if (std::filesystem::exists(modelDir / "model.onnx") &&
        std::filesystem::exists(modelDir / "tokens.txt")) {

        // Check for espeak-ng-data to distinguish Piper
        if (std::filesystem::exists(modelDir / "espeak-ng-data")) {
            return ModelType::Piper;
        }

        // Check directory name for hints
        std::string dirName = WideToUTF8(modelDir.filename().wstring());
        if (dirName.find("piper") != std::string::npos) {
            return ModelType::Piper;
        }
        if (dirName.find("mms") != std::string::npos) {
            return ModelType::MMS;
        }
        if (dirName.find("vits") != std::string::npos ||
            dirName.find("coqui") != std::string::npos) {
            return ModelType::Vits;
        }

        // Default to VITS for unknown model.onnx + tokens.txt combinations
        return ModelType::Vits;
    }

    return ModelType::Unknown;
}

std::string Models::FindOnnxFile(const std::filesystem::path& dir,
                                  const std::string& pattern)
{
    if (!std::filesystem::exists(dir)) {
        return "";
    }

    try {
        for (const auto& entry : std::filesystem::directory_iterator(dir)) {
            if (entry.is_regular_file()) {
                std::string filename = entry.path().filename().string();
                if (filename.find(pattern) == 0 && filename.find(".onnx") != std::string::npos) {
                    return WideToUTF8(entry.path().wstring());
                }
            }
        }
    } catch (...) {
        // Directory access error
    }

    return "";
}

bool Models::HasRequiredFiles(const std::filesystem::path& dir)
{
    ModelType type = DetectModelType(dir);

    switch (type) {
        case ModelType::Matcha: {
            // Need model-steps-X.onnx, vocoder, and tokens.txt
            std::string acousticModel = FindOnnxFile(dir, "model-steps");
            std::string vocoder = FindOnnxFile(dir, "vocos");
            if (vocoder.empty()) {
                vocoder = FindOnnxFile(dir, "vocoder");
            }
            return !acousticModel.empty() &&
                   !vocoder.empty() &&
                   std::filesystem::exists(dir / "tokens.txt");
        }

        case ModelType::Kokoro:
            // Need model.onnx, voices.bin, and tokens.txt
            return std::filesystem::exists(dir / "model.onnx") &&
                   std::filesystem::exists(dir / "voices.bin") &&
                   std::filesystem::exists(dir / "tokens.txt");

        case ModelType::Vits:
        case ModelType::Piper:
        case ModelType::MMS:
        default:
            // Need model.onnx and tokens.txt
            return std::filesystem::exists(dir / "model.onnx") &&
                   std::filesystem::exists(dir / "tokens.txt");
    }
}

std::string Models::WideToUTF8(const std::wstring& wstr)
{
    if (wstr.empty()) {
        return "";
    }

    int size_needed = WideCharToMultiByte(CP_UTF8, 0, &wstr[0],
                                          (int)wstr.size(), nullptr, 0,
                                          nullptr, nullptr);
    std::string strTo(size_needed, 0);
    WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(),
                       &strTo[0], size_needed, nullptr, nullptr);
    return strTo;
}

std::wstring Models::UTF8ToWide(const std::string& str)
{
    if (str.empty()) {
        return L"";
    }

    int size_needed = MultiByteToWideChar(CP_UTF8, 0, &str[0],
                                          (int)str.size(), nullptr, 0);
    std::wstring wstrTo(size_needed, 0);
    MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(),
                       &wstrTo[0], size_needed);
    return wstrTo;
}

} // namespace SherpaOnnx
