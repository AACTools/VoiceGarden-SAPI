#include "SherpaOnnxModels.h"
#include <algorithm>
#include <cctype>
#include <fstream>
#include <map>
#include <regex>
#include <set>
#include <shlobj.h>
#include <sstream>
#include <windows.h>
#include <nlohmann/json.hpp>

// External symbol for the DLL base address
extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace SherpaOnnx {

namespace {
std::string ToLowerCopy(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return value;
}

std::string ToUpperCopy(std::string value) {
    std::transform(value.begin(), value.end(), value.begin(),
                   [](unsigned char c) { return static_cast<char>(std::toupper(c)); });
    return value;
}

std::string TrimCopy(const std::string& value) {
    size_t first = value.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) {
        return "";
    }
    size_t last = value.find_last_not_of(" \t\r\n");
    return value.substr(first, last - first + 1);
}

std::string CollapseSpaces(const std::string& value) {
    std::string out;
    out.reserve(value.size());
    bool previousSpace = false;
    for (char c : value) {
        if (std::isspace(static_cast<unsigned char>(c)) != 0) {
            if (!previousSpace) {
                out.push_back(' ');
                previousSpace = true;
            }
        } else {
            out.push_back(c);
            previousSpace = false;
        }
    }
    return TrimCopy(out);
}

std::string GuessLanguageCodeFromName(const std::string& nameLower) {
    static const std::vector<std::pair<std::string, std::string>> hints = {
        {"english", "en"}, {"french", "fr"},   {"german", "de"},   {"spanish", "es"},
        {"italian", "it"}, {"portuguese", "pt"}, {"chinese", "zh"}, {"japanese", "ja"},
        {"korean", "ko"},  {"arabic", "ar"},   {"russian", "ru"},  {"turkish", "tr"},
        {"vietnamese", "vi"}, {"polish", "pl"}, {"ukrainian", "uk"}, {"dutch", "nl"}
    };

    for (const auto& [needle, code] : hints) {
        if (nameLower.find(needle) != std::string::npos) {
            return code;
        }
    }
    return "";
}

std::string ExtractLanguageString(const nlohmann::json& c) {
    if (!c.contains("language")) {
        return "";
    }

    const auto& languageNode = c["language"];
    if (languageNode.is_string()) {
        return languageNode.get<std::string>();
    }

    if (languageNode.is_array()) {
        for (const auto& item : languageNode) {
            if (item.is_string()) {
                return item.get<std::string>();
            }
            if (item.is_object()) {
                if (item.contains("lang_code") && item["lang_code"].is_string()) {
                    return item["lang_code"].get<std::string>();
                }
                if (item.contains("locale") && item["locale"].is_string()) {
                    return item["locale"].get<std::string>();
                }
            }
        }
    }

    return "";
}
}  // namespace

std::vector<VoiceInfo> Models::DiscoverModels(
    const std::vector<std::wstring>& searchPaths)
{
    return DiscoverModelsWithErrors(searchPaths).first;
}

std::pair<std::vector<VoiceInfo>, std::vector<ModelScanError>> Models::DiscoverModelsWithErrors(
    const std::vector<std::wstring>& searchPaths)
{
    std::vector<VoiceInfo> voices;
    std::vector<ModelScanError> errors;

    wchar_t localAppPath[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA,
                                   nullptr, 0, localAppPath))) {
        std::wstring configPath =
            std::wstring(localAppPath) + L"\\NaturalVoiceSAPIAdapter\\engines_config.json";

        if (std::filesystem::exists(configPath)) {
            voices = LoadFromConfigJson(configPath);
            if (!voices.empty()) {
                return {voices, errors};
            }
        }
    }

    std::set<std::string> seenNames;
    for (const auto& searchPath : searchPaths) {
        if (!std::filesystem::exists(searchPath)) {
            continue;
        }

        try {
            for (const auto& entry : std::filesystem::directory_iterator(searchPath)) {
                if (!entry.is_directory()) {
                    continue;
                }

                VoiceInfo info = ParseModelDirectory(entry.path());
                if (info.name.empty()) {
                    continue;
                }

                std::string validationError;
                if (ValidateModel(info, validationError)) {
                    if (seenNames.insert(info.name).second) {
                        voices.push_back(std::move(info));
                    }
                } else {
                    errors.push_back({
                        info.name,
                        validationError.empty() ? "Model is missing required files" : validationError
                    });
                }
            }
        } catch (const std::exception& ex) {
            errors.push_back({
                WideToUTF8(searchPath),
                std::string("Failed to scan path: ") + ex.what()
            });
        }
    }

    return {voices, errors};
}

bool Models::ValidateModel(const VoiceInfo& info)
{
    std::string error;
    return ValidateModel(info, error);
}

bool Models::ValidateModel(const VoiceInfo& info, std::string& error)
{
    switch (info.modelType) {
        case ModelType::Matcha:
            if (!std::filesystem::exists(info.acousticModelPath)) {
                error = "Missing Matcha acoustic model";
                return false;
            }
            if (!std::filesystem::exists(info.vocoderPath)) {
                error = "Missing Matcha vocoder model";
                return false;
            }
            if (!std::filesystem::exists(info.tokensPath)) {
                error = "Missing Matcha tokens.txt";
                return false;
            }
            try {
                if (std::filesystem::file_size(info.acousticModelPath) < 1024 * 1024) {
                    error = "Matcha acoustic model file is too small";
                    return false;
                }
                if (std::filesystem::file_size(info.vocoderPath) < 1024 * 1024) {
                    error = "Matcha vocoder file is too small";
                    return false;
                }
            } catch (...) {
                error = "Failed reading Matcha model file metadata";
                return false;
            }
            return true;

        case ModelType::Kokoro:
            if (!std::filesystem::exists(info.modelPath)) {
                error = "Missing Kokoro model.onnx";
                return false;
            }
            if (!std::filesystem::exists(info.voicesPath)) {
                error = "Missing Kokoro voices.bin";
                return false;
            }
            if (!std::filesystem::exists(info.tokensPath)) {
                error = "Missing Kokoro tokens.txt";
                return false;
            }
            try {
                if (std::filesystem::file_size(info.modelPath) < 1024 * 1024) {
                    error = "Kokoro model file is too small";
                    return false;
                }
                if (std::filesystem::file_size(info.voicesPath) < 1024 * 1024) {
                    error = "Kokoro voices file is too small";
                    return false;
                }
            } catch (...) {
                error = "Failed reading Kokoro model file metadata";
                return false;
            }
            return true;

        case ModelType::Vits:
        case ModelType::Piper:
        case ModelType::MMS:
        default:
            if (!std::filesystem::exists(info.modelPath)) {
                error = "Missing model.onnx";
                return false;
            }
            if (!std::filesystem::exists(info.tokensPath)) {
                error = "Missing tokens.txt";
                return false;
            }
            try {
                if (std::filesystem::file_size(info.modelPath) < 1024 * 1024) {
                    error = "Model file is too small";
                    return false;
                }
            } catch (...) {
                error = "Failed reading model file metadata";
                return false;
            }
            return true;
    }
}

std::vector<std::wstring> Models::GetDefaultModelPaths()
{
    std::vector<std::wstring> paths;

    wchar_t localAppPath[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA,
                                   nullptr, 0, localAppPath))) {
        paths.push_back(std::wstring(localAppPath) + L"\\NaturalVoiceSAPIAdapter\\models");
    }

    wchar_t programDataPath[MAX_PATH];
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_COMMON_APPDATA,
                                   nullptr, 0, programDataPath))) {
        paths.push_back(std::wstring(programDataPath) +
                        L"\\NaturalVoiceSAPIAdapter\\models");
    }

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

        std::stringstream buffer;
        buffer << configFile.rdbuf();
        configFile.close();

        nlohmann::json config = nlohmann::json::parse(buffer.str());

        if (!config.contains("engines") || !config["engines"].is_object()) {
            return voices;
        }

        for (auto& [key, engine] : config["engines"].items()) {
            std::string type = ToLowerCopy(engine.value("type", ""));
            if (type != "sherpa-onnx" && type != "sherpaonnx") {
                continue;
            }

            if (!engine.contains("config") || !engine["config"].is_object()) {
                continue;
            }

            auto& c = engine["config"];

            VoiceInfo info;
            info.name = c.value("voiceName", key);
            info.modelType = ParseModelType(c.value("modelType", c.value("model_type", "vits")));
            info.language = NormalizeLocale(c.value("locale", ExtractLanguageString(c)));
            if (info.language.empty()) {
                info.language = InferLocaleFromName(info.name);
            }

            info.displayName = c.value("displayName", "");
            if (info.displayName.empty()) {
                info.displayName = BuildDisplayName(info.name, info.language);
            }

            info.speakerCount = c.value("speakers", c.value("speakerCount", 1));
            info.sampleRate = c.value("sampleRate", 22050);

            switch (info.modelType) {
                case ModelType::Matcha:
                    info.acousticModelPath = c.value("acousticModel", c.value("modelPath", ""));
                    info.vocoderPath = c.value("vocoder", c.value("vocoderPath", ""));
                    info.tokensPath = c.value("tokens", c.value("tokensPath", ""));
                    info.dataDir = c.value("dataDir", "");
                    break;

                case ModelType::Kokoro:
                    info.modelPath = c.value("modelPath", c.value("model", ""));
                    info.voicesPath = c.value("voices", c.value("voicesPath", ""));
                    info.tokensPath = c.value("tokens", c.value("tokensPath", ""));
                    info.dataDir = c.value("dataDir", "");
                    break;

                case ModelType::Piper:
                case ModelType::MMS:
                case ModelType::Vits:
                default:
                    info.modelPath = c.value("modelPath", c.value("model", ""));
                    info.tokensPath = c.value("tokensPath", c.value("tokens", ""));
                    info.dataDir = c.value("dataDir", "");
                    break;
            }

            std::string validationError;
            if (ValidateModel(info, validationError)) {
                voices.push_back(std::move(info));
            }
        }
    } catch (const std::exception&) {
        return {};
    }

    return voices;
}

VoiceInfo Models::ParseModelDirectory(const std::filesystem::path& modelDir)
{
    VoiceInfo info;

    info.name = WideToUTF8(modelDir.filename().wstring());
    info.modelType = DetectModelType(modelDir);

    switch (info.modelType) {
        case ModelType::Matcha: {
            info.acousticModelPath = FindOnnxFile(modelDir, "model-steps");
            info.vocoderPath = FindOnnxFile(modelDir, "vocos");
            if (info.vocoderPath.empty()) {
                info.vocoderPath = FindOnnxFile(modelDir, "vocoder");
            }
            info.tokensPath = WideToUTF8((modelDir / "tokens.txt").wstring());

            std::filesystem::path dataDir = modelDir / "espeak-ng-data";
            if (std::filesystem::exists(dataDir)) {
                info.dataDir = WideToUTF8(dataDir.wstring());
            }
            break;
        }

        case ModelType::Kokoro: {
            info.modelPath = WideToUTF8((modelDir / "model.onnx").wstring());
            info.voicesPath = WideToUTF8((modelDir / "voices.bin").wstring());
            info.tokensPath = WideToUTF8((modelDir / "tokens.txt").wstring());
            break;
        }

        case ModelType::Vits:
        case ModelType::Piper:
        case ModelType::MMS:
        default: {
            info.modelPath = WideToUTF8((modelDir / "model.onnx").wstring());
            info.tokensPath = WideToUTF8((modelDir / "tokens.txt").wstring());

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

    info.language = InferLocaleFromName(info.name);
    info.displayName = BuildDisplayName(info.name, info.language);

    return info;
}

ModelType Models::DetectModelType(const std::filesystem::path& modelDir)
{
    if (std::filesystem::exists(modelDir / "model.onnx") &&
        std::filesystem::exists(modelDir / "voices.bin")) {
        return ModelType::Kokoro;
    }

    bool hasModelSteps = false;
    bool hasVocoder = false;
    for (const auto& entry : std::filesystem::directory_iterator(modelDir)) {
        if (!entry.is_regular_file()) {
            continue;
        }
        std::string filename = ToLowerCopy(entry.path().filename().string());
        if (filename.find("model-steps") == 0 && filename.find(".onnx") != std::string::npos) {
            hasModelSteps = true;
        }
        if ((filename.find("vocos") == 0 || filename.find("vocoder") == 0) &&
            filename.find(".onnx") != std::string::npos) {
            hasVocoder = true;
        }
    }
    if (hasModelSteps && hasVocoder) {
        return ModelType::Matcha;
    }

    if (std::filesystem::exists(modelDir / "model.onnx") &&
        std::filesystem::exists(modelDir / "tokens.txt")) {
        if (std::filesystem::exists(modelDir / "espeak-ng-data")) {
            return ModelType::Piper;
        }

        std::string dirName = ToLowerCopy(WideToUTF8(modelDir.filename().wstring()));
        if (dirName.find("piper") != std::string::npos) {
            return ModelType::Piper;
        }
        if (dirName.find("mms") != std::string::npos) {
            return ModelType::MMS;
        }
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

    const std::string patternLower = ToLowerCopy(pattern);
    try {
        for (const auto& entry : std::filesystem::directory_iterator(dir)) {
            if (!entry.is_regular_file()) {
                continue;
            }
            std::string filename = ToLowerCopy(entry.path().filename().string());
            if (filename.find(patternLower) == 0 && filename.find(".onnx") != std::string::npos) {
                return WideToUTF8(entry.path().wstring());
            }
        }
    } catch (...) {
    }

    return "";
}

bool Models::HasRequiredFiles(const std::filesystem::path& dir)
{
    ModelType type = DetectModelType(dir);

    switch (type) {
        case ModelType::Matcha: {
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
            return std::filesystem::exists(dir / "model.onnx") &&
                   std::filesystem::exists(dir / "voices.bin") &&
                   std::filesystem::exists(dir / "tokens.txt");

        case ModelType::Vits:
        case ModelType::Piper:
        case ModelType::MMS:
        default:
            return std::filesystem::exists(dir / "model.onnx") &&
                   std::filesystem::exists(dir / "tokens.txt");
    }
}

std::string Models::NormalizeLocale(const std::string& locale)
{
    std::string trimmed = TrimCopy(locale);
    if (trimmed.empty()) {
        return "";
    }

    std::replace(trimmed.begin(), trimmed.end(), '_', '-');

    std::vector<std::string> parts;
    std::stringstream ss(trimmed);
    std::string item;
    while (std::getline(ss, item, '-')) {
        if (!item.empty()) {
            parts.push_back(item);
        }
    }

    if (parts.empty()) {
        return "";
    }

    parts[0] = ToLowerCopy(parts[0]);
    if (parts.size() > 1) {
        parts[1] = ToUpperCopy(parts[1]);
    }

    std::string normalized = parts[0];
    if (parts.size() > 1) {
        normalized += "-" + parts[1];
    }
    return normalized;
}

std::string Models::InferLocaleFromName(const std::string& name)
{
    const std::string source = ToLowerCopy(name);

    static const std::regex localePattern(R"(([a-z]{2,3})[-_]([a-z]{2}))", std::regex_constants::icase);
    std::smatch match;
    if (std::regex_search(source, match, localePattern) && match.size() >= 3) {
        std::string lang = ToLowerCopy(match[1].str());
        std::string region = ToUpperCopy(match[2].str());
        return lang + "-" + region;
    }

    static const std::map<std::string, std::string> defaults = {
        {"en", "en-US"}, {"zh", "zh-CN"}, {"es", "es-ES"}, {"fr", "fr-FR"},
        {"de", "de-DE"}, {"it", "it-IT"}, {"pt", "pt-BR"}, {"ja", "ja-JP"},
        {"ko", "ko-KR"}, {"ar", "ar-SA"}, {"ru", "ru-RU"}, {"tr", "tr-TR"},
        {"vi", "vi-VN"}, {"pl", "pl-PL"}, {"uk", "uk-UA"}, {"nl", "nl-NL"}
    };

    for (const auto& [code, locale] : defaults) {
        const std::string needleDash = "-" + code + "-";
        const std::string needleUnderscore = "_" + code + "_";
        if (source.rfind(code + "-", 0) == 0 || source.rfind(code + "_", 0) == 0 ||
            source.find(needleDash) != std::string::npos ||
            source.find(needleUnderscore) != std::string::npos) {
            return locale;
        }
    }

    std::string guessed = GuessLanguageCodeFromName(source);
    auto it = defaults.find(guessed);
    if (it != defaults.end()) {
        return it->second;
    }

    return "en-US";
}

std::string Models::BuildDisplayName(const std::string& modelName, const std::string& locale)
{
    std::string display = modelName;
    std::replace(display.begin(), display.end(), '-', ' ');
    std::replace(display.begin(), display.end(), '_', ' ');
    display = CollapseSpaces(display);

    if (!locale.empty() && display.find(locale) == std::string::npos) {
        display = locale + " " + display;
    }

    if (!display.empty()) {
        display[0] = static_cast<char>(std::toupper(static_cast<unsigned char>(display[0])));
    }

    return display;
}

ModelType Models::ParseModelType(const std::string& modelType)
{
    std::string value = ToLowerCopy(modelType);
    if (value == "matcha" || value == "matcha-tts") {
        return ModelType::Matcha;
    }
    if (value == "kokoro") {
        return ModelType::Kokoro;
    }
    if (value == "piper") {
        return ModelType::Piper;
    }
    if (value == "mms") {
        return ModelType::MMS;
    }
    if (value == "vits" || value.empty()) {
        return ModelType::Vits;
    }
    return ModelType::Unknown;
}

std::string Models::WideToUTF8(const std::wstring& wstr)
{
    if (wstr.empty()) {
        return "";
    }

    int size_needed = WideCharToMultiByte(CP_UTF8, 0, &wstr[0],
                                          static_cast<int>(wstr.size()), nullptr, 0,
                                          nullptr, nullptr);
    std::string strTo(size_needed, 0);
    WideCharToMultiByte(CP_UTF8, 0, &wstr[0], static_cast<int>(wstr.size()),
                        &strTo[0], size_needed, nullptr, nullptr);
    return strTo;
}

std::wstring Models::UTF8ToWide(const std::string& str)
{
    if (str.empty()) {
        return L"";
    }

    int size_needed = MultiByteToWideChar(CP_UTF8, 0, &str[0],
                                          static_cast<int>(str.size()), nullptr, 0);
    std::wstring wstrTo(size_needed, 0);
    MultiByteToWideChar(CP_UTF8, 0, &str[0], static_cast<int>(str.size()),
                        &wstrTo[0], size_needed);
    return wstrTo;
}

} // namespace SherpaOnnx