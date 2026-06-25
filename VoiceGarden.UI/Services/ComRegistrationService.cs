using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Handles COM registration/unregistration of the C++ SAPI adapter DLL.
/// </summary>
public static class ComRegistrationService
{
    private const string TtsEngineClsid = "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}";

    public static string GetInstallDir(bool is64Bit)
    {
        return is64Bit
            ? @"C:\Program Files (x86)\VoiceGardenSAPI\x64"
            : @"C:\Program Files (x86)\VoiceGardenSAPI\x86";
    }

    public static bool IsInstalled(bool is64Bit)
    {
        var dir = GetInstallDir(is64Bit);
        var dll = System.IO.Path.Combine(dir, "NaturalVoiceSAPIAdapter.dll");
        return System.IO.File.Exists(dll);
    }

    public static bool IsRegistered(bool is64Bit)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Classes\CLSID\{TtsEngineClsid}\InprocServer32");
        if (key?.GetValue("") is string dllPath)
        {
            return dllPath.Contains(is64Bit ? @"\x64\" : @"\x86\");
        }
        return false;
    }

    public static int Register(bool is64Bit)
    {
        var dir = GetInstallDir(is64Bit);
        var dll = System.IO.Path.Combine(dir, "NaturalVoiceSAPIAdapter.dll");
        if (!System.IO.File.Exists(dll)) return -1;
        return RunElevated("regsvr32", $"/s \"{dll}\"");
    }

    public static int Unregister(bool is64Bit)
    {
        var dir = GetInstallDir(is64Bit);
        var dll = System.IO.Path.Combine(dir, "NaturalVoiceSAPIAdapter.dll");
        if (!System.IO.File.Exists(dll)) return -1;
        return RunElevated("regsvr32", $"/u /s \"{dll}\"");
    }

    public static int RunElevated(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        };

        try
        {
            var p = Process.Start(psi);
            p?.WaitForExit(30000);
            return p?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    public static int RunElevatedWithOutput(string exe, string args, out string output)
    {
        output = "";
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            var p = Process.Start(psi);
            if (p == null) return -1;
            output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);
            return p.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
