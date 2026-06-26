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
        var dll = System.IO.Path.Combine(dir, "VoiceGardenSAPIAdapter.dll");
        return System.IO.File.Exists(dll);
    }

    public static bool IsRegistered(bool is64Bit)
    {
        // 64-bit: HKLM\SOFTWARE\Classes\CLSID\...
        // 32-bit: HKLM\SOFTWARE\WOW6432Node\Classes\CLSID\...
        var basePath = is64Bit
            ? $@"SOFTWARE\Classes\CLSID\{TtsEngineClsid}\InprocServer32"
            : $@"SOFTWARE\WOW6432Node\Classes\CLSID\{TtsEngineClsid}\InprocServer32";
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(basePath);
        return key?.GetValue("") is string dllPath && !string.IsNullOrEmpty(dllPath);
    }

    public static int Register(bool is64Bit)
    {
        var dir = GetInstallDir(is64Bit);
        var dll = System.IO.Path.Combine(dir, "VoiceGardenSAPIAdapter.dll");
        if (!System.IO.File.Exists(dll))
        {
            // Check if the exe directory has the DLL (running from publish/debug)
            var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            var altDll = System.IO.Path.Combine(exeDir, "VoiceGardenSAPIAdapter.dll");
            if (System.IO.File.Exists(altDll))
                dll = altDll;
            else
                return -1;
        }
        // Use 32-bit regsvr32 for 32-bit DLLs
        var regsvr = is64Bit ? "regsvr32" : @"C:\Windows\SysWOW64\regsvr32.exe";
        return RunElevated(regsvr, $"/s \"{dll}\"");
    }

    public static int Unregister(bool is64Bit)
    {
        var dir = GetInstallDir(is64Bit);
        var dll = System.IO.Path.Combine(dir, "VoiceGardenSAPIAdapter.dll");
        if (!System.IO.File.Exists(dll))
        {
            var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            var altDll = System.IO.Path.Combine(exeDir, "VoiceGardenSAPIAdapter.dll");
            if (System.IO.File.Exists(altDll))
                dll = altDll;
            else
                return -1;
        }
        var regsvr = is64Bit ? "regsvr32" : @"C:\Windows\SysWOW64\regsvr32.exe";
        return RunElevated(regsvr, $"/u /s \"{dll}\"");
    }

    public static int RunElevated(string exe, string args)
    {
        try
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

            var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit(60000); // Wait up to 60 seconds for UAC + regsvr32
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return -2; // User cancelled UAC
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
