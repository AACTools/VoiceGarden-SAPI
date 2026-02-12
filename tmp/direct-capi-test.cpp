#include <windows.h>
#include <iostream>
#include <string>
#include "SherpaOnnx/libs/sherpa-onnx-v1.12.23-win-x64-shared/include/sherpa-onnx/c-api/c-api.h"

int main() {
  const char* local = getenv("LOCALAPPDATA");
  if (!local) return 10;
  std::string base = std::string(local) + "\\NaturalVoiceSAPIAdapter\\models\\piper-en-alan-low\\vits-piper-en_GB-alan-low";
  std::string model = base + "\\en_GB-alan-low.onnx";
  std::string tokens = base + "\\tokens.txt";
  std::string data = base + "\\espeak-ng-data";

  HMODULE h = LoadLibraryA("sherpa-onnx-c-api.dll");
  if (!h) { std::cerr << "load failed\n"; return 1; }

  auto create = (const SherpaOnnxOfflineTts* (*)(const SherpaOnnxOfflineTtsConfig*))GetProcAddress(h, "SherpaOnnxCreateOfflineTts");
  auto destroy = (void (*)(const SherpaOnnxOfflineTts*))GetProcAddress(h, "SherpaOnnxDestroyOfflineTts");
  auto gen = (const SherpaOnnxGeneratedAudio* (*)(const SherpaOnnxOfflineTts*, const char*, int32_t, float))GetProcAddress(h, "SherpaOnnxOfflineTtsGenerate");
  auto freeAudio = (void (*)(const SherpaOnnxGeneratedAudio*))GetProcAddress(h, "SherpaOnnxDestroyOfflineTtsGeneratedAudio");
  if (!create || !destroy || !gen || !freeAudio) { std::cerr << "proc missing\n"; return 2; }

  SherpaOnnxOfflineTtsConfig cfg = {};
  cfg.model.vits.model = model.c_str();
  cfg.model.vits.lexicon = "";
  cfg.model.vits.tokens = tokens.c_str();
  cfg.model.vits.data_dir = data.c_str();
  cfg.model.vits.noise_scale = 0.667f;
  cfg.model.vits.noise_scale_w = 0.8f;
  cfg.model.vits.length_scale = 1.0f;
  cfg.model.vits.dict_dir = "";
  cfg.model.num_threads = 2;
  cfg.model.debug = 1;
  cfg.model.provider = "cpu";
  cfg.model.matcha = {};
  cfg.model.kokoro = {};
  cfg.model.kitten = {};
  cfg.model.zipvoice = {};
  cfg.rule_fsts = "";
  cfg.max_num_sentences = 1;
  cfg.rule_fars = "";
  cfg.silence_scale = 0.2f;

  std::cout << "creating..." << std::endl;
  const SherpaOnnxOfflineTts* tts = create(&cfg);
  if (!tts) { std::cerr << "create failed\n"; return 3; }
  std::cout << "created" << std::endl;

  const SherpaOnnxGeneratedAudio* a = gen(tts, "direct c-api smoke", 0, 1.0f);
  if (!a) { std::cerr << "generate failed\n"; destroy(tts); return 4; }
  std::cout << "samples=" << a->n << " sr=" << a->sample_rate << std::endl;
  freeAudio(a);
  destroy(tts);
  return 0;
}
