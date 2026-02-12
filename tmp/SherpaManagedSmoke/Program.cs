using SherpaOnnx;

var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var modelDir = Path.Combine(root, "NaturalVoiceSAPIAdapter", "models", "piper-en-alan-low", "vits-piper-en_GB-alan-low");
var model = Path.Combine(modelDir, "en_GB-alan-low.onnx");
var tokens = Path.Combine(modelDir, "tokens.txt");
var dataDir = Path.Combine(modelDir, "espeak-ng-data");

Console.WriteLine($"model={model}");
Console.WriteLine($"tokens={tokens}");
Console.WriteLine($"dataDir={dataDir}");

var config = new OfflineTtsConfig();
config.Model.Vits.Model = model;
config.Model.Vits.Tokens = tokens;
config.Model.Vits.DataDir = dataDir;
config.Model.NumThreads = 2;
config.Model.Provider = "cpu";
config.Model.Debug = 1;

Console.WriteLine("creating tts...");
var tts = new OfflineTts(config);
Console.WriteLine("created");
var audio = tts.Generate("Managed smoke test.", 1.0f, 0);
Console.WriteLine($"samples={audio.Samples.Length} sr={audio.SampleRate}");
