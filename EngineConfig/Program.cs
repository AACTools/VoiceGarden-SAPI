using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;

namespace EngineConfig;

internal static class Program
{
    private static readonly string SapiTokensRoot = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";
    private static readonly string TtsEngineClsid = "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}";

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "engines" => ListEngines(),
                "voices" => ListVoices(args),
                "validate" => ValidateCredentials(args),
                "promote" => PromoteVoice(args),
                "unpromote" => UnpromoteVoice(args),
                "promoted" => ListPromoted(),
                "test" => TestVoice(args),
                "-h" or "--help" or "/?" => ShowHelp(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int ListEngines()
    {
        Console.WriteLine("Supported TTS Engines");
        Console.WriteLine("=====================");
        Console.WriteLine();

        var engines = new[]
        {
            ("azure", "Azure Cognitive Services Speech", new[] { "AZURE_SPEECH_KEY", "AZURE_SPEECH_REGION" }),
            ("openai", "OpenAI TTS", new[] { "OPENAI_API_KEY" }),
            ("elevenlabs", "ElevenLabs", new[] { "ELEVENLABS_API_KEY" }),
            ("google", "Google Cloud TTS", new[] { "GOOGLE_API_KEY" }),
            ("polly", "AWS Polly", new[] { "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_REGION" }),
            ("cartesia", "Cartesia", new[] { "CARTESIA_API_KEY" }),
            ("deepgram", "Deepgram", new[] { "DEEPGRAM_API_KEY" }),
            ("sherpaonnx", "SherpaOnnx (Offline)", Array.Empty<string>() ),
        };

        foreach (var (id, name, requiredKeys) in engines)
        {
            var configured = requiredKeys.All(k => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)));
            var status = requiredKeys.Length == 0 ? "ready" : configured ? "configured" : "not configured";
            var color = configured ? ConsoleColor.Green : ConsoleColor.Gray;
            Console.ForegroundColor = color;
            Console.WriteLine($"  {id,-15} {name,-35} [{status}]");
            Console.ResetColor();
            if (requiredKeys.Length > 0 && !configured)
            {
                Console.WriteLine($"                  Requires: {string.Join(", ", requiredKeys)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Usage: EngineConfig.exe voices --engine <engine> --key <key> [--region <region>]");
        return 0;
    }

    private static int ListVoices(string[] args)
    {
        var opts = ParseArgs(args);
        if (string.IsNullOrEmpty(opts.Engine))
        {
            Console.Error.WriteLine("Error: --engine is required");
            Console.Error.WriteLine("Usage: voices --engine <engine> --key <key> [--region <region>]");
            return 1;
        }

        var creds = BuildCredentials(opts.Engine, opts.Key, opts.Region);
        if (creds == null)
        {
            Console.Error.WriteLine($"Error: Unknown engine '{opts.Engine}'");
            return 1;
        }

        Console.WriteLine($"Fetching voices for engine '{opts.Engine}'...");

        var client = DotNetTtsWrapper.Models.TtsFactory.CreateClient(opts.Engine, creds);
        if (client == null)
        {
            Console.Error.WriteLine($"Error: Could not create client for engine '{opts.Engine}'");
            return 1;
        }

        var voices = client.GetVoicesAsync().GetAwaiter().GetResult();

        if (opts.Json)
        {
            var json = JsonSerializer.Serialize(voices.Select(v => new
            {
                id = v.Id,
                name = v.Name,
                language = v.LanguageCodes?.FirstOrDefault()?.Bcp47 ?? "en-US",
                gender = v.Gender.ToString(),
                provider = v.Provider ?? opts.Engine
            }), new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"Found {voices.Count} voices:");
            Console.WriteLine();
            foreach (var voice in voices)
            {
                var lang = voice.LanguageCodes?.FirstOrDefault()?.Bcp47 ?? "en-US";
                Console.WriteLine($"  {voice.Id,-40} {voice.Name,-30} {lang}");
            }
        }

        return 0;
    }

    private static int ValidateCredentials(string[] args)
    {
        var opts = ParseArgs(args);
        if (string.IsNullOrEmpty(opts.Engine))
        {
            Console.Error.WriteLine("Error: --engine is required");
            Console.Error.WriteLine("Usage: validate --engine <engine> --key <key> [--region <region>]");
            return 1;
        }

        var creds = BuildCredentials(opts.Engine, opts.Key, opts.Region);
        if (creds == null)
        {
            Console.Error.WriteLine($"Error: Unknown engine '{opts.Engine}'");
            return 1;
        }

        Console.WriteLine($"Validating credentials for '{opts.Engine}'...");

        var client = DotNetTtsWrapper.Models.TtsFactory.CreateClient(opts.Engine, creds);
        if (client == null)
        {
            Console.Error.WriteLine($"Error: Could not create client for engine '{opts.Engine}'");
            return 1;
        }

        // Try CheckCredentialsAsync first (fast path)
        try
        {
            var result = client.CheckCredentialsAsync().GetAwaiter().GetResult();
            if (result.IsValid)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Credentials valid! ({result.AvailableVoiceCount} voices available)");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Credentials invalid: {result.ErrorMessage}");
                Console.ResetColor();
                return 2;
            }
        }
        catch (Exception ex)
        {
            // CheckCredentialsAsync may not actually hit the API (hardcoded voice lists)
            // Fall through to real validation via a tiny synthesis attempt
            Console.WriteLine($"  CheckCredentialsAsync inconclusive ({ex.Message}), trying synthesis...");
        }

        // Real validation: attempt a tiny synthesis
        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"validate_{opts.Engine}_{Guid.NewGuid():N}.wav");
            client.SynthToFileAsync("test", tempFile).GetAwaiter().GetResult();
            var file = new FileInfo(tempFile);
            if (file.Exists && file.Length > 0)
            {
                file.Delete();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  Credentials valid! (synthesis test succeeded)");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  Credentials invalid: synthesis produced no audio");
                Console.ResetColor();
                return 2;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            var status = ex.StatusCode?.ToString() ?? "unknown";
            Console.WriteLine($"  Credentials invalid: HTTP {status}");
            if (status == "Unauthorized" || status == "Forbidden")
                Console.WriteLine("  API key is wrong, expired, or lacks permissions");
            Console.ResetColor();
            return 2;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Credentials invalid: {ex.Message}");
            Console.ResetColor();
            return 2;
        }
    }

    private static int PromoteVoice(string[] args)
    {
        var opts = ParseArgs(args);

        if (string.IsNullOrEmpty(opts.Engine) || string.IsNullOrEmpty(opts.VoiceId))
        {
            Console.Error.WriteLine("Error: --engine and --voice are required");
            Console.Error.WriteLine("Usage: promote --engine <engine> --voice <voice-id> --key <key> [--region <region>]");
            return 1;
        }

        var creds = BuildCredentials(opts.Engine, opts.Key, opts.Region);
        if (creds == null)
        {
            Console.Error.WriteLine($"Error: Unknown engine '{opts.Engine}'");
            return 1;
        }

        var tokenName = $"Cloud-{opts.Engine}-{opts.VoiceId}".Replace("/", "_").Replace("\\", "_");
        var tokenPath = $@"{SapiTokensRoot}\{tokenName}";

        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(tokenPath);
        if (key == null)
        {
            Console.Error.WriteLine("Error: Cannot create HKLM token (admin required)");
            return 1;
        }

        key.SetValue("", $"{opts.Engine} {opts.VoiceId}", Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("CLSID", TtsEngineClsid, Microsoft.Win32.RegistryValueKind.String);

        using var configKey = key.CreateSubKey("VoiceGardenConfig");
        configKey.SetValue("EngineType", CapitalizeEngine(opts.Engine), Microsoft.Win32.RegistryValueKind.String);
        configKey.SetValue("Voice", opts.VoiceId, Microsoft.Win32.RegistryValueKind.String);
        configKey.SetValue("Key", opts.Key ?? "", Microsoft.Win32.RegistryValueKind.String);
        if (!string.IsNullOrEmpty(opts.Region))
            configKey.SetValue("Region", opts.Region, Microsoft.Win32.RegistryValueKind.String);
        configKey.SetValue("IsCloudVoice", 1, Microsoft.Win32.RegistryValueKind.DWord);

        // For Azure, also save key/region to the old registry location so the
        // C++ adapter's VoiceTokenEnumerator can enumerate Azure voices
        if (opts.Engine.Equals("azure", StringComparison.OrdinalIgnoreCase))
        {
            using var enumKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"SOFTWARE\VoiceGardenSAPIAdapter\Enumerator");
            if (enumKey != null && !string.IsNullOrEmpty(opts.Key))
            {
                enumKey.SetValue("AzureVoiceKey", opts.Key, Microsoft.Win32.RegistryValueKind.String);
                if (!string.IsNullOrEmpty(opts.Region))
                    enumKey.SetValue("AzureVoiceRegion", opts.Region, Microsoft.Win32.RegistryValueKind.String);
            }
        }

        using var attrsKey = key.CreateSubKey("Attributes");
        attrsKey.SetValue("Name", opts.VoiceId, Microsoft.Win32.RegistryValueKind.String);
        attrsKey.SetValue("Gender", "Neutral", Microsoft.Win32.RegistryValueKind.String);
        attrsKey.SetValue("Age", "Adult", Microsoft.Win32.RegistryValueKind.String);
        attrsKey.SetValue("Language", "0409", Microsoft.Win32.RegistryValueKind.String);
        attrsKey.SetValue("Locale", opts.Locale ?? "en-US", Microsoft.Win32.RegistryValueKind.String);
        attrsKey.SetValue("Vendor", CapitalizeEngine(opts.Engine), Microsoft.Win32.RegistryValueKind.String);
        attrsKey.SetValue("VoiceGardenType", "Cloud", Microsoft.Win32.RegistryValueKind.String);

        Console.WriteLine($"Promoted {opts.Engine}/{opts.VoiceId} to HKLM token: {tokenName}");
        return 0;
    }

    private static int UnpromoteVoice(string[] args)
    {
        var opts = ParseArgs(args);
        if (string.IsNullOrEmpty(opts.VoiceId))
        {
            Console.Error.WriteLine("Error: --voice (token name) is required");
            return 1;
        }

        var tokenPath = $@"{SapiTokensRoot}\{opts.VoiceId}";
        try
        {
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(tokenPath);
            Console.WriteLine($"Removed HKLM token: {opts.VoiceId}");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine($"Token not found: {opts.VoiceId}");
            return 1;
        }
    }

    private static int ListPromoted()
    {
        using var tokens = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SapiTokensRoot);
        if (tokens == null)
        {
            Console.WriteLine("No promoted voices found.");
            return 0;
        }

        var cloudTokens = tokens.GetSubKeyNames()
            .Where(n => n.StartsWith("Cloud-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cloudTokens.Count == 0)
        {
            Console.WriteLine("No promoted cloud voices found.");
            return 0;
        }

        Console.WriteLine("Promoted Cloud Voices (HKLM):");
        Console.WriteLine();
        foreach (var name in cloudTokens)
        {
            using var tk = tokens.OpenSubKey(name);
            using var cfg = tk?.OpenSubKey("VoiceGardenConfig");
            var engine = cfg?.GetValue("EngineType") as string ?? "?";
            var voiceId = cfg?.GetValue("VoiceId") as string ?? "?";
            Console.WriteLine($"  {name,-50} {engine,-15} {voiceId}");
        }

        return 0;
    }

    private static int TestVoice(string[] args)
    {
        var opts = ParseArgs(args);

        if (string.IsNullOrEmpty(opts.Engine) || string.IsNullOrEmpty(opts.VoiceId))
        {
            Console.Error.WriteLine("Error: --engine and --voice are required");
            Console.Error.WriteLine("Usage: test --engine <engine> --voice <voice-id> --key <key> [--region <region>] --text \"Hello\"");
            return 1;
        }

        var creds = BuildCredentials(opts.Engine, opts.Key, opts.Region);
        if (creds == null)
        {
            Console.Error.WriteLine($"Error: Unknown engine '{opts.Engine}'");
            return 1;
        }

        var client = DotNetTtsWrapper.Models.TtsFactory.CreateClient(opts.Engine, creds);
        if (client == null)
        {
            Console.Error.WriteLine($"Error: Could not create client for engine '{opts.Engine}'");
            return 1;
        }

        client.SetVoice(opts.VoiceId);

        var text = opts.Text ?? "Hello world, this is a test.";
        var outputPath = opts.Output ?? Path.Combine(Path.GetTempPath(), "engineconfig_test.wav");

        Console.WriteLine($"Synthesizing '{text}' with {opts.Engine}/{opts.VoiceId}...");

        client.SynthToFileAsync(text, outputPath).GetAwaiter().GetResult();

        var file = new FileInfo(outputPath);
        if (file.Exists && file.Length > 0)
        {
            Console.WriteLine($"Audio saved: {outputPath} ({file.Length} bytes)");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("Synthesis produced no audio");
            return 1;
        }
    }

    private static DotNetTtsWrapper.Models.ITtsCredentials? BuildCredentials(string engine, string? key, string? region)
    {
        return engine.ToLowerInvariant() switch
        {
            "azure" => new DotNetTtsWrapper.Models.AzureCredentials
            {
                SubscriptionKey = key ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ?? "",
                Region = region ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ?? "eastus"
            },
            "openai" => new DotNetTtsWrapper.Models.OpenAICredentials { ApiKey = key ?? "" },
            "elevenlabs" => new DotNetTtsWrapper.Models.ElevenLabsCredentials { ApiKey = key ?? "" },
            "google" => new DotNetTtsWrapper.Models.GoogleCredentials { ApiKey = key ?? "" },
            "polly" => new DotNetTtsWrapper.Models.PollyCredentials
            {
                AccessKeyId = key ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "",
                SecretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "",
                Region = region ?? Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1"
            },
            "cartesia" => new DotNetTtsWrapper.Models.CartesiaCredentials { ApiKey = key ?? "" },
            "deepgram" => new DotNetTtsWrapper.Models.DeepgramCredentials { ApiKey = key ?? "" },
            "sherpaonnx" => new DotNetTtsWrapper.Models.SherpaOnnxCredentials(),
            _ => null
        };
    }

    private static string CapitalizeEngine(string engine) => engine.ToLowerInvariant() switch
    {
        "azure" => "Azure",
        "openai" => "OpenAI",
        "elevenlabs" => "ElevenLabs",
        "google" => "Google",
        "polly" => "Polly",
        "cartesia" => "Cartesia",
        "deepgram" => "DeepGram",
        "sherpaonnx" => "Sherpa",
        _ => engine
    };

    private static CliOptions ParseArgs(string[] args)
    {
        var opts = new CliOptions();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--engine" when i + 1 < args.Length:
                    opts.Engine = args[++i]; break;
                case "--voice" when i + 1 < args.Length:
                    opts.VoiceId = args[++i]; break;
                case "--key" when i + 1 < args.Length:
                    opts.Key = args[++i]; break;
                case "--region" when i + 1 < args.Length:
                    opts.Region = args[++i]; break;
                case "--locale" when i + 1 < args.Length:
                    opts.Locale = args[++i]; break;
                case "--text" when i + 1 < args.Length:
                    opts.Text = args[++i]; break;
                case "--output" when i + 1 < args.Length:
                    opts.Output = args[++i]; break;
                case "--json":
                    opts.Json = true; break;
            }
        }
        return opts;
    }

    private static int ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("EngineConfig - Multi-Engine TTS Voice Manager");
        Console.WriteLine("=============================================");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  EngineConfig.exe <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  engines                           List supported engines and config status");
        Console.WriteLine("  voices --engine <id>              List voices for an engine");
        Console.WriteLine("         --key <key>                API key for the engine");
        Console.WriteLine("         [--region <region>]        Region (Azure/Polly)");
        Console.WriteLine("         [--json]                   Output as JSON");
        Console.WriteLine();
        Console.WriteLine("  promote --engine <id>             Register a voice as HKLM SAPI token");
        Console.WriteLine("          --voice <voice-id>        Voice ID to promote");
        Console.WriteLine("          --key <key>               API key");
        Console.WriteLine("          [--region <region>]       Region");
        Console.WriteLine("          [--locale <locale>]       BCP-47 locale (default: en-US)");
        Console.WriteLine();
        Console.WriteLine("  unpromote --voice <token-name>    Remove a promoted voice from HKLM");
        Console.WriteLine();
        Console.WriteLine("  promoted                          List all promoted cloud voices");
        Console.WriteLine();
        Console.WriteLine("  test --engine <id>                Test synthesis with an engine");
        Console.WriteLine("       --voice <voice-id>           Voice to use");
        Console.WriteLine("       --key <key>                  API key");
        Console.WriteLine("       [--text \"Hello world\"]       Text to synthesize");
        Console.WriteLine("       [--output <path>]            Output WAV file");
        Console.WriteLine();
        Console.WriteLine("Supported Engines:");
        Console.WriteLine("  azure, openai, elevenlabs, google, polly, cartesia, deepgram, sherpaonnx");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  EngineConfig.exe engines");
        Console.WriteLine("  EngineConfig.exe voices --engine azure --key YOUR_KEY --region eastus");
        Console.WriteLine("  EngineConfig.exe voices --engine openai --key sk-xxx --json");
        Console.WriteLine("  EngineConfig.exe promote --engine azure --voice en-US-JennyNeural --key YOUR_KEY --region eastus");
        Console.WriteLine("  EngineConfig.exe test --engine openai --voice alloy --key sk-xxx --text \"Hello\"");
        Console.WriteLine();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: '{command}'");
        Console.Error.WriteLine();
        ShowHelp();
        return 1;
    }

    private class CliOptions
    {
        public string? Engine { get; set; }
        public string? VoiceId { get; set; }
        public string? Key { get; set; }
        public string? Region { get; set; }
        public string? Locale { get; set; }
        public string? Text { get; set; }
        public string? Output { get; set; }
        public bool Json { get; set; }
    }
}
