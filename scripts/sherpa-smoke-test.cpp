#include "../SherpaOnnx/SherpaOnnxConfig.h"
#include "../SherpaOnnx/SherpaOnnxEngine.h"

#include <algorithm>
#include <iostream>
#include <string>
#include <unordered_map>
#include <vector>

namespace {
std::unordered_map<std::string, std::string> ParseArgs(int argc, char** argv) {
    std::unordered_map<std::string, std::string> args;
    for (int i = 1; i < argc; ++i) {
        std::string key = argv[i];
        if (!key.starts_with("--")) {
            continue;
        }
        if (i + 1 >= argc) {
            args[key] = "";
            continue;
        }
        args[key] = argv[++i];
    }
    return args;
}

std::string GetOr(const std::unordered_map<std::string, std::string>& args,
                  const std::string& key,
                  const std::string& fallback = "") {
    auto it = args.find(key);
    return it == args.end() ? fallback : it->second;
}
}  // namespace

int main(int argc, char** argv) {
    std::cout << "SMOKE_BEGIN\n" << std::flush;
    auto args = ParseArgs(argc, argv);
    std::string modelType = GetOr(args, "--model-type", "vits");
    std::string text = GetOr(args, "--text", "Smoke test.");

    SherpaOnnx::ModelConfig cfg;
    cfg.provider = GetOr(args, "--provider", "cpu");
    cfg.numThreads = 2;
    cfg.debug = true;
    cfg.maxNumSentences = 1;
    cfg.silenceScale = 0.2f;
    cfg.voiceName = GetOr(args, "--voice-name", "smoke");

    std::transform(modelType.begin(), modelType.end(), modelType.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });

    if (modelType == "matcha") {
        cfg.modelType = SherpaOnnx::TtsModelType::Matcha;
        cfg.matcha.acousticModel = GetOr(args, "--acoustic-model");
        cfg.matcha.vocoder = GetOr(args, "--vocoder");
        cfg.matcha.tokens = GetOr(args, "--tokens");
        cfg.matcha.lexicon = GetOr(args, "--lexicon");
        cfg.matcha.dataDir = GetOr(args, "--data-dir");
        cfg.matcha.dictDir = GetOr(args, "--dict-dir");
    } else if (modelType == "kokoro") {
        cfg.modelType = SherpaOnnx::TtsModelType::Kokoro;
        cfg.kokoro.model = GetOr(args, "--model");
        cfg.kokoro.voices = GetOr(args, "--voices");
        cfg.kokoro.tokens = GetOr(args, "--tokens");
        cfg.kokoro.lexicon = GetOr(args, "--lexicon");
        cfg.kokoro.dataDir = GetOr(args, "--data-dir");
        cfg.kokoro.dictDir = GetOr(args, "--dict-dir");
        cfg.kokoro.lang = GetOr(args, "--lang");
    } else {
        cfg.modelType = SherpaOnnx::TtsModelType::Vits;
        cfg.vits.model = GetOr(args, "--model");
        cfg.vits.tokens = GetOr(args, "--tokens");
        cfg.vits.lexicon = GetOr(args, "--lexicon");
        cfg.vits.dataDir = GetOr(args, "--data-dir");
        cfg.vits.dictDir = GetOr(args, "--dict-dir");
    }

    std::cout << "SMOKE_CREATE_START\n" << std::flush;
    SherpaOnnx::Engine engine(cfg);
    std::cout << "SMOKE_CREATE_DONE\n" << std::flush;
    if (!engine.IsValid()) {
        std::cerr << "SMOKE_FAIL create: " << engine.GetLastError() << "\n";
        return 2;
    }

    auto audio = engine.Generate(text, 1.0f);
    if (audio.empty()) {
        std::cerr << "SMOKE_FAIL generate: " << engine.GetLastError() << "\n";
        return 3;
    }

    std::cout << "SMOKE_OK samples=" << audio.size() << " sr=" << engine.GetSampleRate() << "\n";
    return 0;
}
