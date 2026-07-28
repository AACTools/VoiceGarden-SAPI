using Avalonia;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoiceGarden.UI;

internal class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private static Mutex? _singleInstanceMutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // CLI mode if any arguments provided
        if (args.Length > 0)
        {
            AttachConsole(-1); // Attach to parent console
            var exitCode = CliDispatcher.Run(args);
            Environment.Exit(exitCode);
        }

        // Prevent multiple instances of the GUI
        _singleInstanceMutex = new Mutex(true, "VoiceGarden.UI.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Another instance is running — try to bring it to the foreground
            BringExistingInstanceToFront();
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void BringExistingInstanceToFront()
    {
        try
        {
            // Find the existing VoiceGarden.UI window by enumerating top-level windows
            var found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == Environment.ProcessId)
                    return true; // skip ourselves

                var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                // Check if this window belongs to another VoiceGarden.UI process
                try
                {
                    var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                    if (proc.MainModule?.ModuleName == currentProc.MainModule?.ModuleName)
                    {
                        found = hWnd;
                        return false; // stop enumerating
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero)
            {
                ShowWindow(found, SW_RESTORE);
                SetForegroundWindow(found);
            }
        }
        catch { }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
