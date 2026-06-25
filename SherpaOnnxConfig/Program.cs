using System;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace SherpaOnnxConfig
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        private static extern bool SetStdHandle(int nStdHandle, IntPtr handle);

        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;

        private static System.IO.StreamWriter? consoleWriter;
        private static readonly string ProbeLogPath = Path.Combine(AppDataLayout.AdapterDataDir, "sapi-probe.log");

        [STAThread]
        static void Main(string[] args)
        {
            bool rescanGui = args.Length > 0 && args[0].Equals("rescan-gui", StringComparison.OrdinalIgnoreCase);

            // Check if running in CLI mode (no GUI arguments or explicitly --cli)
            bool useCli = (args.Length > 0 && !rescanGui) ||
                          Environment.GetEnvironmentVariable("SherpaOnnxCLI") == "1";

            if (useCli)
            {
                // CLI mode - allocate console if not already attached
                bool consoleAllocated = false;
                if (!AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    if (AllocConsole())
                    {
                        consoleAllocated = true;
                        // Redirect stdout to the new console
                        IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                        if (stdHandle != IntPtr.Zero)
                        {
                            var safeHandle = new SafeFileHandle(stdHandle, ownsHandle: false);
                            FileStream fs = new FileStream(safeHandle, FileAccess.Write);
                            StreamWriter writer = new StreamWriter(fs)
                            {
                                AutoFlush = true
                            };
                            consoleWriter = writer;
                            Console.SetOut(writer);
                            Console.SetError(writer);
                        }
                    }
                }

                int exitCode = RunCli(args);

                // Keep console open briefly if we allocated it
                if (consoleAllocated && exitCode != 0)
                {
                    Console.WriteLine("\nPress Enter to exit...");
                    Console.ReadLine();
                }

                consoleWriter?.Dispose();
                if (consoleAllocated)
                {
                    FreeConsole();
                }

                Environment.Exit(exitCode);
                return;
            }
            else
            {
                // GUI mode
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(rescanGui));
            }
        }

        private static int RunCli(string[] args)
        {
            if (args.Length == 0)
            {
                ShowCliHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string? languageFilter = null;

            // Parse --language flag
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("--language", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    languageFilter = args[i + 1];
                    i++; // Skip the language value
                }
            }

            switch (command)
            {
                case "list":
                    return MainForm.ListVoices(languageFilter);

                case "download":
                    // Find the model ID (first non-flag argument)
                    string? modelId = null;
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (!args[i].StartsWith("--"))
                        {
                            modelId = args[i];
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(modelId))
                    {
                        Console.WriteLine("ERROR: 'download' command requires a model ID.");
                        Console.WriteLine("\nUse 'list' command to see available models.");
                        return 1;
                    }
                    return MainForm.DownloadModel(modelId);

                case "downloaded":
                    return MainForm.ListDownloaded();

                case "rescan":
                    return MainForm.RescanModels();

                case "promote-hklm":
                    {
                        string? promoteModelId = null;
                        string? promoteModelDir = null;
                        bool promoteCompatEnUs = false;
                        for (int i = 1; i < args.Length; i++)
                        {
                            if (args[i].Equals("--model-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                            {
                                promoteModelDir = args[++i];
                                continue;
                            }
                            if (args[i].Equals("--compat-en-us", StringComparison.OrdinalIgnoreCase))
                            {
                                promoteCompatEnUs = true;
                                continue;
                            }
                            if (!args[i].StartsWith("--"))
                            {
                                // Keep scanning so trailing flags like --model-dir are still parsed.
                                promoteModelId ??= args[i];
                                continue;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(promoteModelId))
                        {
                            Console.WriteLine("ERROR: 'promote-hklm' command requires a model ID.");
                            return 1;
                        }

                        return MainForm.PromoteModelTokenToHklm(promoteModelId, promoteModelDir, promoteCompatEnUs);
                    }

                case "promote-all":
                    {
                        bool promoteCompatEnUs = false;
                        bool adminAll = false;
                        for (int i = 1; i < args.Length; i++)
                        {
                            if (args[i].Equals("--compat-en-us", StringComparison.OrdinalIgnoreCase))
                                promoteCompatEnUs = true;
                            if (args[i].Equals("--admin", StringComparison.OrdinalIgnoreCase))
                                adminAll = true;
                        }
                        return MainForm.PromoteAllDownloadedToHklm(promoteCompatEnUs, adminAll);
                    }

                case "sapi-probe":
                    {
                        string? probeVoiceId = null;
                        string probeText = "The quick brown fox jumps over the lazy dog.";
                        int timeoutSec = 30;

                        for (int i = 1; i < args.Length; i++)
                        {
                            if (args[i].Equals("--voice", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                            {
                                probeVoiceId = args[++i];
                            }
                            else if (args[i].Equals("--text", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                            {
                                probeText = args[++i];
                            }
                            else if (args[i].Equals("--timeout", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                            {
                                if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                                {
                                    timeoutSec = parsed;
                                }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(probeVoiceId))
                        {
                            Console.WriteLine("ERROR: sapi-probe requires --voice <voice-id>.");
                            return 1;
                        }

                        return RunSapiProbe(probeVoiceId, probeText, timeoutSec);
                    }

                case "-h":
                case "--help":
                case "/?":
                    ShowCliHelp();
                    return 0;

                default:
                    Console.WriteLine($"ERROR: Unknown command '{command}'");
                    ShowCliHelp();
                    return 1;
            }
        }

        private static void ShowCliHelp()
        {
            Console.WriteLine();
            Console.WriteLine("SherpaOnnx Model Manager - Command Line Interface");
            Console.WriteLine("=====================================");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  SherpaOnnxConfig.exe [command] [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  list                    List all available models");
            Console.WriteLine("  list --language <lang>   List models for specific language");
            Console.WriteLine("  download <model-id>       Download a model by ID");
            Console.WriteLine("  downloaded               List downloaded models");
            Console.WriteLine("  rescan                   Validate local model folders and show per-model errors");
                    Console.WriteLine("  promote-hklm <model-id> [--model-dir <path>] [--compat-en-us]  Install one model token to HKLM");
                    Console.WriteLine("  promote-all [--compat-en-us] [--admin]                          Install all downloaded models to HKLM");
            Console.WriteLine("  sapi-probe --voice <id>   Probe SAPI activation/speak stages for one voice");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  SherpaOnnxConfig.exe list");
            Console.WriteLine("  SherpaOnnxConfig.exe list --language English");
            Console.WriteLine("  SherpaOnnxConfig.exe list --language Chinese");
            Console.WriteLine("  SherpaOnnxConfig.exe download kokoro-en-en-19");
            Console.WriteLine("  SherpaOnnxConfig.exe promote-hklm piper-en-alan-low");
            Console.WriteLine("  SherpaOnnxConfig.exe promote-hklm piper-en-alan-low --model-dir \"C:\\Users\\WillWade\\AppData\\Local\\<InstallFolder>\\models\\piper-en-alan-low\"");
            Console.WriteLine("  SherpaOnnxConfig.exe promote-hklm piper-en-alan-low --compat-en-us");
            Console.WriteLine("  SherpaOnnxConfig.exe sapi-probe --voice piper-en-alan-low");
            Console.WriteLine("  SherpaOnnxConfig.exe downloaded");
            Console.WriteLine();
            Console.WriteLine("Without arguments, the GUI will launch.");
            Console.WriteLine();
            Console.WriteLine("Models are downloaded to:");
            Console.WriteLine($"  {AppDataLayout.ModelsDir}\\");
        }

        private static int RunSapiProbe(string voiceId, string text, int timeoutSeconds)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            TryResetProbeLog();

            Thread thread = new Thread(() =>
            {
                object? voiceObj = null;
                object? voices = null;
                object? selectedToken = null;
                try
                {
                    ProbeWrite($"[probe] voice-id={voiceId}");
                    ProbeWrite("[probe] stage=create SpVoice");
                    Type? spVoiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                    if (spVoiceType == null)
                    {
                        ProbeWrite("[probe] FAIL: SAPI.SpVoice ProgID not found");
                        tcs.TrySetResult(2);
                        return;
                    }

                    voiceObj = Activator.CreateInstance(spVoiceType);
                    if (voiceObj == null)
                    {
                        ProbeWrite("[probe] FAIL: could not instantiate SpVoice");
                        tcs.TrySetResult(3);
                        return;
                    }
                    ProbeWrite("[probe] OK: SpVoice created");

                    ProbeWrite("[probe] stage=GetVoices(Vendor=K2FSA)");
                    voices = InvokeComMethod(voiceObj, "GetVoices", "Vendor=K2FSA", "");
                    if (voices == null)
                    {
                        ProbeWrite("[probe] FAIL: GetVoices returned null");
                        tcs.TrySetResult(4);
                        return;
                    }

                    int count = Convert.ToInt32(GetComProperty(voices, "Count") ?? 0, CultureInfo.InvariantCulture);
                    ProbeWrite($"[probe] voices-count={count}");
                    for (int i = 0; i < count; i++)
                    {
                        object? v = null;
                        try
                        {
                            v = InvokeComMethod(voices, "Item", i);
                            string id = GetComProperty(v, "Id")?.ToString() ?? "<null>";
                            ProbeWrite($"[probe] item[{i}] id={id}");
                            if (id.IndexOf(voiceId, StringComparison.OrdinalIgnoreCase) >= 0 && selectedToken == null)
                            {
                                selectedToken = v;
                                v = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            ProbeWrite($"[probe] item[{i}] err={ex.GetType().Name}: {ex.Message}");
                        }
                        finally
                        {
                            ReleaseComObject(v);
                        }
                    }

                    if (selectedToken == null)
                    {
                        ProbeWrite("[probe] FAIL: target token not found in collection");
                        tcs.TrySetResult(5);
                        return;
                    }

                    ProbeWrite("[probe] stage=set Voice");
                    SetComProperty(voiceObj, "Voice", selectedToken);
                    ProbeWrite("[probe] OK: Voice set");

                    ProbeWrite("[probe] stage=Speak(async)");
                    // SPF_ASYNC = 1 so the call should return quickly and still validate activation.
                    object? ret = InvokeComMethod(voiceObj, "Speak", text, 1);
                    ProbeWrite($"[probe] OK: Speak returned {ret}");
                    tcs.TrySetResult(0);
                }
                catch (Exception ex)
                {
                    Exception root = ex;
                    while (root is TargetInvocationException tie && tie.InnerException != null)
                    {
                        root = tie.InnerException;
                    }

                    if (root is COMException comEx)
                    {
                        ProbeWrite($"[probe] COM FAIL: {comEx.Message} HRESULT=0x{comEx.HResult:X8}");
                    }
                    else
                    {
                        ProbeWrite($"[probe] FAIL: {root.GetType().Name}: {root.Message}");
                    }
                    tcs.TrySetResult(10);
                }
                finally
                {
                    ReleaseComObject(selectedToken);
                    ReleaseComObject(voices);
                    ReleaseComObject(voiceObj);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!tcs.Task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                ProbeWrite($"[probe] TIMEOUT after {timeoutSeconds}s");
                return 124;
            }

            return tcs.Task.Result;
        }

        private static void ProbeWrite(string line)
        {
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
            }

            try
            {
                string? dir = Path.GetDirectoryName(ProbeLogPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(ProbeLogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void TryResetProbeLog()
        {
            try
            {
                string? dir = Path.GetDirectoryName(ProbeLogPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(ProbeLogPath, $"=== sapi-probe {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
            }
            catch
            {
            }
        }

        private static object? InvokeComMethod(object? target, string name, params object[] args)
        {
            if (target == null)
                return null;
            return target.GetType().InvokeMember(
                name,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                args,
                CultureInfo.InvariantCulture);
        }

        private static object? GetComProperty(object? target, string name)
        {
            if (target == null)
                return null;
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                null,
                CultureInfo.InvariantCulture);
        }

        private static void SetComProperty(object? target, string name, object? value)
        {
            if (target == null)
                return;
            target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                new[] { value },
                CultureInfo.InvariantCulture);
        }

        private static void ReleaseComObject(object? obj)
        {
            if (obj == null)
                return;
            try
            {
                if (Marshal.IsComObject(obj))
                    Marshal.FinalReleaseComObject(obj);
            }
            catch
            {
            }
        }
    }
}
