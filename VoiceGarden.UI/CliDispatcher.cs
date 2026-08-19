using System;
using System.Collections.Generic;

namespace VoiceGarden.UI;

/// <summary>
/// CLI dispatcher for headless operation.
/// </summary>
public static class CliDispatcher
{
    public static int Run(string[] args)
    {
        if (args.Length == 0) return 0;

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "install" => RunInstall(args),
                "uninstall" => RunUninstall(args),
                "status" => RunStatus(),
                "voices" => RunVoices(args),
                "validate" => RunValidate(args),
                "promote" => RunPromote(args),
                "promoted" => RunListPromoted(),
                "unpromote" => RunUnpromote(args),
                "models" => RunModels(args),
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

    private static bool ParsePlatform(string[] args, out bool x64, out bool x86)
    {
        x64 = false; x86 = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--platform", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var p = args[++i].ToLowerInvariant();
                if (p == "x64") x64 = true;
                else if (p == "x86") x86 = true;
                else if (p == "all") { x64 = true; x86 = true; }
            }
        }
        if (!x64 && !x86) { x64 = true; x86 = true; } // default: all
        return true;
    }

    private static int RunInstall(string[] args)
    {
        ParsePlatform(args, out var x64, out var x86);
        int rc = 0;
        if (x64) { var r = Services.ComRegistrationService.Register(true); Console.WriteLine($"64-bit register: {(r == 0 ? "OK" : "FAILED")}"); if (r != 0) rc = r; }
        if (x86) { var r = Services.ComRegistrationService.Register(false); Console.WriteLine($"32-bit register: {(r == 0 ? "OK" : "FAILED")}"); if (r != 0) rc = r; }
        return rc;
    }

    private static int RunUninstall(string[] args)
    {
        ParsePlatform(args, out var x64, out var x86);
        int rc = 0;
        if (x64) { var r = Services.ComRegistrationService.Unregister(true); Console.WriteLine($"64-bit unregister: {(r == 0 ? "OK" : "FAILED")}"); if (r != 0) rc = r; }
        if (x86) { var r = Services.ComRegistrationService.Unregister(false); Console.WriteLine($"32-bit unregister: {(r == 0 ? "OK" : "FAILED")}"); if (r != 0) rc = r; }
        return rc;
    }

    private static int RunStatus()
    {
        var r64 = Services.ComRegistrationService.IsRegistered(true);
        var r32 = Services.ComRegistrationService.IsRegistered(false);
        Console.WriteLine($"64-bit adapter: {(r64 ? "Registered" : "Not registered")}");
        Console.WriteLine($"32-bit adapter: {(r32 ? "Registered" : "Not registered")}");
        return 0;
    }

    private static int ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("VoiceGarden — SAPI Voice Adapter Configuration");
        Console.WriteLine("==============================================");
        Console.WriteLine();
        Console.WriteLine("Usage: VoiceGarden.UI.exe [command] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  (none)            Launch GUI");
        Console.WriteLine("  install           Register COM adapter");
        Console.WriteLine("    --platform X    x64, x86, or all (default: all)");
        Console.WriteLine("  uninstall         Unregister COM adapter");
        Console.WriteLine("    --platform X    x64, x86, or all");
        Console.WriteLine("  status            Show registration status");
        Console.WriteLine();
        Console.WriteLine("  voices            List voices for an engine");
        Console.WriteLine("    --engine <id>   azure, openai, elevenlabs, google, polly, cartesia, deepgram");
        Console.WriteLine("    --key <key>     API key");
        Console.WriteLine("    [--region <r>]  Region (Azure/Polly)");
        Console.WriteLine("    [--json]        Output as JSON");
        Console.WriteLine();
        Console.WriteLine("  validate          Validate credentials");
        Console.WriteLine("    --engine <id> --key <key> [--region <r>]");
        Console.WriteLine();
        Console.WriteLine("  promote           Install a voice as HKLM SAPI token");
        Console.WriteLine("    --engine <id> --voice <voice-id> --key <key> [--region <r>]");
        Console.WriteLine();
        Console.WriteLine("  promoted          List all promoted voices");
        Console.WriteLine("  unpromote         Remove a promoted voice");
        Console.WriteLine("    --voice <token-name>");
        Console.WriteLine();
        Console.WriteLine("  models            SherpaOnnx model management");
        Console.WriteLine("    list            Show installed models");
        Console.WriteLine("    download <id>   Download a model from the catalog");
        Console.WriteLine("    promote-all     Install all models to HKLM");
        Console.WriteLine("    rescan          Refresh installed model status");
        Console.WriteLine();
        Console.WriteLine("Run without arguments to launch the GUI.");
        Console.WriteLine();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: '{command}'");
        ShowHelp();
        return 1;
    }

    private static Dictionary<string, string> ParseArgs(string[] args, int startIndex = 1)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length)
            {
                result[args[i].Substring(2)] = args[++i];
            }
        }
        return result;
    }

    private static int RunVoices(string[] args)
    {
        var opts = ParseArgs(args);
        if (!opts.TryGetValue("engine", out var engine) || !opts.TryGetValue("key", out var key))
        {
            Console.Error.WriteLine("Error: --engine and --key are required");
            return 1;
        }
        opts.TryGetValue("region", out var region);
        var asJson = args.Contains("--json");

        var creds = BuildRustCredentials(engine, key, region ?? "");
        if (creds == null) { Console.Error.WriteLine($"Unknown engine: {engine}"); return 1; }

        using var client = new RustTtsWrapper.TtsClient(engine, creds);

        Console.Error.WriteLine($"Fetching voices for {engine}...");
        var voices = client.GetVoices();
        Console.Error.WriteLine($"Found {voices.Count} voices");

        if (asJson)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(voices.Select(v => new
            {
                id = v.Id, name = v.Name,
                language = string.IsNullOrEmpty(v.Language) ? "en-US" : v.Language,
                gender = v.Gender ?? "Unknown",
                provider = v.Engine ?? engine
            }));
            Console.WriteLine(json);
        }
        else
        {
            foreach (var v in voices)
            {
                var lang = string.IsNullOrEmpty(v.Language) ? "en-US" : v.Language;
                Console.WriteLine($"  {v.Id,-40} {v.Name,-30} {lang}");
            }
        }
        return 0;
    }

    private static int RunValidate(string[] args)
    {
        var opts = ParseArgs(args);
        if (!opts.TryGetValue("engine", out var engine) || !opts.TryGetValue("key", out var key))
        {
            Console.Error.WriteLine("Error: --engine and --key are required");
            return 1;
        }
        opts.TryGetValue("region", out var region);

        var creds = BuildRustCredentials(engine, key, region ?? "");
        if (creds == null) { Console.Error.WriteLine($"Unknown engine: {engine}"); return 1; }

        Console.Write($"Validating {engine} credentials... ");
        try
        {
            using var client = new RustTtsWrapper.TtsClient(engine, creds);
            var voices = client.GetVoices();
            Console.WriteLine($"OK ({voices.Count} voices)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static int RunPromote(string[] args)
    {
        var opts = ParseArgs(args);
        if (!opts.TryGetValue("engine", out var engine) || !opts.TryGetValue("voice", out var voice))
        {
            Console.Error.WriteLine("Error: --engine and --voice are required");
            return 1;
        }
        opts.TryGetValue("key", out var key);
        opts.TryGetValue("region", out var region);

        if (Services.VoicePromotionService.Promote(engine, voice, key ?? "", region))
        {
            Console.WriteLine($"Promoted {engine}/{voice} to HKLM");
            return 0;
        }
        Console.Error.WriteLine("Failed (admin required for HKLM)");
        return 1;
    }

    private static int RunListPromoted()
    {
        var promoted = Services.VoicePromotionService.ListPromoted();
        if (promoted.Count == 0)
        {
            Console.WriteLine("No promoted voices found.");
            return 0;
        }
        Console.WriteLine("Promoted Voices (HKLM):");
        foreach (var p in promoted)
        {
            Console.WriteLine($"  {p.TokenName,-50} {p.Engine,-15} {p.VoiceId}");
        }
        return 0;
    }

    private static int RunUnpromote(string[] args)
    {
        var opts = ParseArgs(args);
        if (!opts.TryGetValue("voice", out var voice))
        {
            Console.Error.WriteLine("Error: --voice (token name) is required");
            return 1;
        }
        if (Services.VoicePromotionService.Unpromote(voice))
        {
            Console.WriteLine($"Removed: {voice}");
            return 0;
        }
        Console.Error.WriteLine($"Failed (admin required for HKLM)");
        return 1;
    }

    /// <summary>
    /// Build wrapper credentials via the shared TtsCredentialBuilder so the
    /// CLI and the UI cannot drift apart on engine credential shapes.
    /// </summary>
    private static Dictionary<string, string>? BuildRustCredentials(string engine, string key, string region)
        => Services.TtsCredentialBuilder.Build(engine, key, region);

    private static int RunModels(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: models <list|download|promote-all|rescan>");
            return 1;
        }

        var sub = args[1].ToLowerInvariant();
        return sub switch
        {
            "list" => RunModelsList(),
            "download" => RunModelsDownload(args),
            "promote-all" => RunModelsPromoteAll(),
            "rescan" => RunModelsRescan(),
            _ => UnknownCommand($"models {sub}")
        };
    }

    private static int RunModelsList()
    {
        var installed = Services.SherpaModelService.ScanInstalledModels();
        Console.WriteLine($"Installed models: {installed.Count}");
        foreach (var m in installed)
        {
            Console.WriteLine($"  {m.Id,-30} Model: {(m.ModelPath != null ? "OK" : "MISSING")}  Promoted: {m.IsPromoted}");
        }
        return 0;
    }

    private static int RunModelsDownload(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Error: model ID required. Usage: models download <id>");
            return 1;
        }
        var modelId = args[2];
        var catalog = Services.SherpaModelService.LoadCatalogAsync().GetAwaiter().GetResult();
        var model = catalog.Find(c => c.Id == modelId);
        if (model == null)
        {
            Console.Error.WriteLine($"Model '{modelId}' not found in catalog");
            return 1;
        }
        Console.WriteLine($"Downloading {modelId}...");
        Services.SherpaModelService.DownloadModelAsync(model).GetAwaiter().GetResult();
        Console.WriteLine("Done");
        return 0;
    }

    private static int RunModelsPromoteAll()
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "VoiceGarden_elevated_test.log"),
                $"Running elevated at {DateTime.Now}\nUser: {Environment.UserName}\n");
        }
        catch { }

        var (promoted, failed) = Services.SherpaModelService.PromoteAll();
        Console.WriteLine($"Promoted {promoted} model(s), failed {failed}");
        return failed > 0 ? 1 : 0;
    }

    private static int RunModelsRescan()
    {
        var installed = Services.SherpaModelService.ScanInstalledModels();
        Console.WriteLine($"Found {installed.Count} installed models:");
        foreach (var m in installed)
        {
            Console.WriteLine($"  {m.Id,-30} Promoted: {m.IsPromoted}");
        }
        return 0;
    }
}
