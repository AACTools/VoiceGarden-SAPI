using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

        [STAThread]
        static void Main(string[] args)
        {
            // Check if running in CLI mode (no GUI arguments or explicitly --cli)
            bool useCli = args.Length > 0 ||
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
                            System.IO.FileStream fs = new System.IO.FileStream(stdHandle, System.IO.FileAccess.Write);
                            System.IO.StreamWriter writer = new System.IO.StreamWriter(fs)
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
                Application.Run(new MainForm());
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
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  SherpaOnnxConfig.exe list");
            Console.WriteLine("  SherpaOnnxConfig.exe list --language English");
            Console.WriteLine("  SherpaOnnxConfig.exe list --language Chinese");
            Console.WriteLine("  SherpaOnnxConfig.exe download kokoro-en-en-19");
            Console.WriteLine("  SherpaOnnxConfig.exe downloaded");
            Console.WriteLine();
            Console.WriteLine("Without arguments, the GUI will launch.");
            Console.WriteLine();
            Console.WriteLine("Models are downloaded to:");
            Console.WriteLine($"  {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\OpenSpeech\\models\\");
        }
    }
}
