param(
    [Parameter(Mandatory = $true)]
    [string]$VoiceId,
    [string]$Text = "The quick brown fox jumps over the lazy dog.",
    [int]$TimeoutSeconds = 30,
    [switch]$Audible
)

$ErrorActionPreference = "Stop"

$code = @"
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

public static class SapiProbeRunner
{
    public sealed class ProbeResult
    {
        public int ExitCode;
        public List<string> Lines = new List<string>();
    }

    public static ProbeResult Run(string voiceId, string text, int timeoutSeconds, bool audible)
    {
        var result = new ProbeResult { ExitCode = 1 };
        bool finished = false;
        Thread thread = new Thread(() =>
        {
            object voiceObj = null;
            object voices = null;
            object selected = null;
            try
            {
                result.Lines.Add("[probe] voice-id=" + voiceId);
                Type spVoiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (spVoiceType == null)
                {
                    result.Lines.Add("[probe] FAIL: ProgID SAPI.SpVoice not found");
                    result.ExitCode = 2;
                    return;
                }

                voiceObj = Activator.CreateInstance(spVoiceType);
                if (voiceObj == null)
                {
                    result.Lines.Add("[probe] FAIL: cannot instantiate SpVoice");
                    result.ExitCode = 3;
                    return;
                }
                result.Lines.Add("[probe] OK: SpVoice created");
                DumpLoadedRuntimeModules(result, "after-spvoice-create");

                voices = InvokeComMethod(voiceObj, "GetVoices", "Vendor=K2FSA", "");
                if (voices == null)
                {
                    result.Lines.Add("[probe] FAIL: GetVoices returned null");
                    result.ExitCode = 4;
                    return;
                }

                int count = Convert.ToInt32(GetComProperty(voices, "Count") ?? 0, CultureInfo.InvariantCulture);
                result.Lines.Add("[probe] voices-count=" + count);
                for (int i = 0; i < count; i++)
                {
                    object token = null;
                    try
                    {
                        token = InvokeComMethod(voices, "Item", i);
                        string id = (GetComProperty(token, "Id") ?? "<null>").ToString();
                        result.Lines.Add("[probe] item[" + i + "]=" + id);
                        if (id.IndexOf(voiceId, StringComparison.OrdinalIgnoreCase) >= 0 && selected == null)
                        {
                            selected = token;
                            token = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Lines.Add("[probe] item[" + i + "] err=" + ex.GetType().Name + ": " + ex.Message);
                    }
                    finally
                    {
                        Release(token);
                    }
                }

                if (selected == null)
                {
                    result.Lines.Add("[probe] FAIL: target token not found");
                    result.ExitCode = 5;
                    return;
                }

                try
                {
                    SetComProperty(voiceObj, "Voice", selected);
                    result.Lines.Add("[probe] OK: Voice set");
                    DumpLoadedRuntimeModules(result, "after-set-voice");
                }
                catch (Exception ex)
                {
                    Exception r = Unwrap(ex);
                    int hr = Marshal.GetHRForException(r);
                    result.Lines.Add("[probe] FAIL: set Voice -> " + r.Message + " HR=0x" + hr.ToString("X8"));
                    result.ExitCode = 6;
                    return;
                }

                try
                {
                    int speakFlags = audible ? 0 : 1; // 0=sync (audible), 1=async
                    object ret = InvokeComMethod(voiceObj, "Speak", text, speakFlags);
                    result.Lines.Add("[probe] OK: Speak(" + (audible ? "sync" : "async") + ") ret=" + (ret == null ? "<null>" : ret.ToString()));
                    DumpLoadedRuntimeModules(result, "after-speak");
                    result.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Exception r = Unwrap(ex);
                    int hr = Marshal.GetHRForException(r);
                    result.Lines.Add("[probe] FAIL: Speak -> " + r.Message + " HR=0x" + hr.ToString("X8"));
                    result.ExitCode = 7;
                }
            }
            catch (Exception ex)
            {
                Exception r = Unwrap(ex);
                int hr = Marshal.GetHRForException(r);
                result.Lines.Add("[probe] FAIL: unhandled " + r.GetType().Name + ": " + r.Message + " HR=0x" + hr.ToString("X8"));
                result.ExitCode = 10;
            }
            finally
            {
                Release(selected);
                Release(voices);
                Release(voiceObj);
                finished = true;
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            result.Lines.Add("[probe] TIMEOUT after " + timeoutSeconds + " sec");
            result.ExitCode = 124;
            return result;
        }
        if (!finished)
        {
            result.Lines.Add("[probe] FAIL: probe thread ended unexpectedly");
            result.ExitCode = 125;
        }
        return result;
    }

    private static Exception Unwrap(Exception ex)
    {
        Exception root = ex;
        while (root is TargetInvocationException && ((TargetInvocationException)root).InnerException != null)
        {
            root = ((TargetInvocationException)root).InnerException;
        }
        return root;
    }

    private static object InvokeComMethod(object target, string name, params object[] args)
    {
        return target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
            null,
            target,
            args,
            CultureInfo.InvariantCulture);
    }

    private static object GetComProperty(object target, string name)
    {
        return target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
            null,
            target,
            null,
            CultureInfo.InvariantCulture);
    }

    private static void SetComProperty(object target, string name, object value)
    {
        target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
            null,
            target,
            new object[] { value },
            CultureInfo.InvariantCulture);
    }

    private static void Release(object obj)
    {
        if (obj == null)
            return;
        try
        {
            if (Marshal.IsComObject(obj))
            {
                Marshal.FinalReleaseComObject(obj);
            }
        }
        catch { }
    }

    private static void DumpLoadedRuntimeModules(ProbeResult result, string stage)
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            result.Lines.Add("[probe] modules@" + stage + ":");
            foreach (ProcessModule m in proc.Modules)
            {
                string name = m.ModuleName ?? string.Empty;
                if (name.IndexOf("onnxruntime", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("sherpa", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("speech", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("NaturalVoiceSAPIAdapter", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Lines.Add("[probe]   " + name + " => " + m.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            result.Lines.Add("[probe] module-dump failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
"@

if (-not ("SapiProbeRunner" -as [type])) {
    Add-Type -TypeDefinition $code -Language CSharp
}
$r = [SapiProbeRunner]::Run($VoiceId, $Text, $TimeoutSeconds, [bool]$Audible)
$r.Lines | ForEach-Object { $_ }
exit $r.ExitCode
