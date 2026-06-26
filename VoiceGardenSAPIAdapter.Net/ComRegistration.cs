using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VoiceGardenSAPIAdapter.SapiInterop;

namespace VoiceGardenSAPIAdapter;

public static class ComRegistration
{
    private static readonly string TokenEnumsPath = @"SOFTWARE\Microsoft\Speech\Voices\TokenEnums\VoiceGardenEnumerator";
    private static readonly string TTSEngineClsidPath = $@"CLSID\{{{SapiClsids.CLSID_TTSEngine}}}";
    private static readonly string EnumeratorClsidPath = $@"CLSID\{{{SapiClsids.CLSID_VoiceTokenEnumerator}}}";

    [ComRegisterFunction]
    public static void Register(Type t)
    {
        string dllPath = GetComHostDllPath();

        RegisterInprocServer(TTSEngineClsidPath, dllPath, "VoiceGardenSAPIAdapter.TTSEngine");
        RegisterInprocServer(EnumeratorClsidPath, dllPath, "VoiceGardenSAPIAdapter.VoiceTokenEnumerator");

        using (var key = Registry.LocalMachine.CreateSubKey(TokenEnumsPath))
        {
            key?.SetValue("CLSID", $"{{{SapiClsids.CLSID_VoiceTokenEnumerator}}}");
        }
    }

    [ComUnregisterFunction]
    public static void Unregister(Type t)
    {
        try { Registry.ClassesRoot.DeleteSubKeyTree(TTSEngineClsidPath); } catch { }
        try { Registry.ClassesRoot.DeleteSubKeyTree(EnumeratorClsidPath); } catch { }
        try { Registry.LocalMachine.DeleteSubKeyTree(TokenEnumsPath); } catch { }
    }

    private static void RegisterInprocServer(string clsidPath, string dllPath, string className)
    {
        using (var key = Registry.ClassesRoot.CreateSubKey(clsidPath))
        {
            key?.SetValue(null, className);
        }

        using (var key = Registry.ClassesRoot.CreateSubKey($@"{clsidPath}\InprocServer32"))
        {
            key?.SetValue(null, dllPath);
            key?.SetValue("ThreadingModel", "Both");
        }
    }

    private static string GetComHostDllPath()
    {
        string assemblyPath = typeof(ComRegistration).Assembly.Location;
        string? dir = System.IO.Path.GetDirectoryName(assemblyPath);
        string comHostName = System.IO.Path.GetFileNameWithoutExtension(assemblyPath) + ".comhost.dll";
        return dir != null
            ? System.IO.Path.Combine(dir, comHostName)
            : comHostName;
    }
}
