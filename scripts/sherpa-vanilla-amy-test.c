#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "sherpa-onnx/c-api/c-api.h"

int main(int argc, char* argv[]) {
  (void)argc;
  (void)argv;

  const char* local = getenv("LOCALAPPDATA");
  if (!local || !*local) {
    fprintf(stderr, "LOCALAPPDATA is not set.\n");
    return 1;
  }

  char model_dir[1024] = {0};
  snprintf(model_dir, sizeof(model_dir),
           "%s\\NaturalVoiceSAPIAdapter\\models\\piper-en-amy-low\\vits-piper-en_US-amy-low",
           local);

  char model_path[1200] = {0};
  char tokens_path[1200] = {0};
  char data_dir[1200] = {0};
  snprintf(model_path, sizeof(model_path), "%s\\en_US-amy-low.onnx", model_dir);
  snprintf(tokens_path, sizeof(tokens_path), "%s\\tokens.txt", model_dir);
  snprintf(data_dir, sizeof(data_dir), "%s\\espeak-ng-data", model_dir);

  SherpaOnnxOfflineTtsConfig config;
  memset(&config, 0, sizeof(config));

  config.model.vits.model = model_path;
  config.model.vits.tokens = tokens_path;
  config.model.vits.data_dir = data_dir;
  config.model.num_threads = 1;
  config.model.provider = "cpu";
  config.max_num_sentences = 1;

  printf("Creating TTS...\n");
  const SherpaOnnxOfflineTts* tts = SherpaOnnxCreateOfflineTts(&config);
  if (!tts) {
    fprintf(stderr, "SherpaOnnxCreateOfflineTts failed.\n");
    return 2;
  }

  const int sid = 0;
  const char* text =
      "Friends fell out often because life was changing so fast. The easiest thing in the world was to lose touch with someone.";

  printf("Generating audio...\n");
  const SherpaOnnxGeneratedAudio* audio =
      SherpaOnnxOfflineTtsGenerate(tts, text, sid, 1.0f);
  if (!audio) {
    fprintf(stderr, "SherpaOnnxOfflineTtsGenerate failed.\n");
    SherpaOnnxDestroyOfflineTts(tts);
    return 3;
  }

  const char* out_wav = "test-amy.wav";
  if (!SherpaOnnxWriteWave(audio->samples, audio->n, audio->sample_rate, out_wav)) {
    fprintf(stderr, "SherpaOnnxWriteWave failed.\n");
    SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
    SherpaOnnxDestroyOfflineTts(tts);
    return 4;
  }

  SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
  SherpaOnnxDestroyOfflineTts(tts);

  printf("Saved to ./%s\n", out_wav);
  return 0;
}
