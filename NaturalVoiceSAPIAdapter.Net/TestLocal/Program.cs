using System;
using System.Runtime.InteropServices;
using NaturalVoiceSAPIAdapter;
using NaturalVoiceSAPIAdapter.SapiInterop;

Console.WriteLine("=== .NET TTS Adapter Local Test ===\n");

Console.WriteLine("1. Testing VoiceTokenEnumerator creation...");
try
{
    var enumerator = new VoiceTokenEnumerator();
    enumerator.GetCount(out uint count);
    Console.WriteLine($"   Found {count} voices");

    for (uint i = 0; i < Math.Min(count, 10); i++)
    {
        enumerator.Item(i, out var token);
        token.GetStringValue(null, out IntPtr pName);
        string name = Marshal.PtrToStringUni(pName) ?? "(null)";
        Marshal.FreeCoTaskMem(pName);

        token.GetStringValue("EngineName", out IntPtr pEngine);
        string engine = Marshal.PtrToStringUni(pEngine) ?? "(null)";
        Marshal.FreeCoTaskMem(pEngine);

        token.GetStringValue("VoiceId", out IntPtr pVoiceId);
        string voiceId = Marshal.PtrToStringUni(pVoiceId) ?? "(null)";
        Marshal.FreeCoTaskMem(pVoiceId);

        Console.WriteLine($"   [{i}] {name} (engine={engine}, voiceId={voiceId})");
    }
    if (count == 0)
        Console.WriteLine("   (No local voices found - this is expected without SherpaOnnx models installed)");
    Console.WriteLine("   OK\n");
}
catch (Exception ex)
{
    Console.WriteLine($"   FAILED: {ex.Message}\n");
}

Console.WriteLine("2. Testing TTSEngine creation + GetOutputFormat...");
try
{
    var engine = new TTSEngine();
    Console.WriteLine("   TTSEngine created OK");

    int hr = engine.GetOutputFormat(
        IntPtr.Zero, IntPtr.Zero,
        out Guid fmtId, out IntPtr pFmt);
    Console.WriteLine($"   GetOutputFormat: hr=0x{hr:X8}");

    if (hr == 0 && pFmt != IntPtr.Zero)
    {
        var wf = Marshal.PtrToStructure<WAVEFORMATEX>(pFmt);
        Console.WriteLine($"   Format: {wf.nSamplesPerSec}Hz, {wf.wBitsPerSample}bit, {wf.nChannels}ch");
        Marshal.FreeCoTaskMem(pFmt);
    }
    Console.WriteLine("   OK\n");
}
catch (Exception ex)
{
    Console.WriteLine($"   FAILED: {ex.Message}\n");
}

Console.WriteLine("3. Testing SpObjectToken + SpDataKey in-memory...");
try
{
    var token = new SpObjectToken();
    token.SetId(null, "TestToken", false);
    token.SetStringValue(null, "Test Voice");
    token.SetStringValue("EngineName", "sherpaonnx");
    token.SetStringValue("VoiceId", "test-voice");

    var config = token.Data.GetOrCreateSubKey("NaturalVoiceConfig");
    config.SetStringValue("ApiKey", "test-key");

    token.GetStringValue(null, out IntPtr pName);
    string name = Marshal.PtrToStringUni(pName!)!;
    Marshal.FreeCoTaskMem(pName);

    token.GetStringValue("EngineName", out IntPtr pEngine);
    string engine = Marshal.PtrToStringUni(pEngine!)!;
    Marshal.FreeCoTaskMem(pEngine);

    Console.WriteLine($"   Token name: {name}, engine: {engine}");

    ISpDataKey configKey;
    token.OpenKey("NaturalVoiceConfig", out configKey);
    configKey.GetStringValue("ApiKey", out IntPtr pKey);
    string key = Marshal.PtrToStringUni(pKey!)!;
    Marshal.FreeCoTaskMem(pKey);
    Console.WriteLine($"   Config ApiKey: {key}");
    Console.WriteLine("   OK\n");
}
catch (Exception ex)
{
    Console.WriteLine($"   FAILED: {ex.Message}\n");
}

Console.WriteLine("4. Testing COM registration (requires admin)...");
try
{
    ComRegistration.Register(typeof(TTSEngine));
    Console.WriteLine("   Register succeeded");

    using (var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(
        @"CLSID\{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}\InprocServer32"))
    {
        if (key != null)
        {
            string? path = key.GetValue(null) as string;
            Console.WriteLine($"   InprocServer32 = {path}");
        }
    }

    ComRegistration.Unregister(typeof(TTSEngine));
    Console.WriteLine("   Unregister succeeded");
    Console.WriteLine("   OK\n");
}
catch (UnauthorizedAccessException)
{
    Console.WriteLine("   SKIPPED: Requires elevated (admin) privileges\n");
}
catch (Exception ex)
{
    Console.WriteLine($"   FAILED: {ex.Message}\n");
}

Console.WriteLine("5. Testing CredentialBuilder...");
try
{
    var dataKey = new SpDataKey();
    dataKey.SetStringValue("ApiKey", "test-api-key-123");
    dataKey.SetStringValue("Region", "eastus");

    var creds = CredentialBuilder.FromTokenConfig("azure", dataKey);
    Console.WriteLine($"   Azure creds: {creds?.GetType().Name ?? "null"}");

    var creds2 = CredentialBuilder.FromTokenConfig("openai", dataKey);
    Console.WriteLine($"   OpenAI creds: {creds2?.GetType().Name ?? "null"}");

    var creds3 = CredentialBuilder.FromTokenConfig("unknown", dataKey);
    Console.WriteLine($"   Unknown engine creds: {creds3?.GetType().Name ?? "null"}");
    Console.WriteLine("   OK\n");
}
catch (Exception ex)
{
    Console.WriteLine($"   FAILED: {ex.Message}\n");
}

Console.WriteLine("=== Tests complete ===");
