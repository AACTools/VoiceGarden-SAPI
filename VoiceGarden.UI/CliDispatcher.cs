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
}
