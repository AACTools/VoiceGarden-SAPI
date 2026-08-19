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
            // Another instance is running. If it has a window, bring it to
            // the front. If it has been running for a while with no window
            // (a zombie holding the mutex), kill it and take over instead of
            // exiting with nothing shown — otherwise the app can never be
            // launched again without manual intervention.
            if (BringExistingInstanceToFront())
                return;
            if (!TryTakeOverFromWindowlessInstance())
                return; // a healthy instance came forward while we waited
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Kill any VoiceGarden.UI process that owns no visible window, then
    /// wait for the single-instance mutex to be released. Returns true when
    /// we can proceed with launching the UI.
    /// </summary>
    private static bool TryTakeOverFromWindowlessInstance()
    {
        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
            {
                if (proc.Id == current.Id) continue;
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero) continue; // has UI — leave it
                    // A just-started instance may not have created its window
                    // yet — only treat long-running windowless processes as
                    // zombies.
                    if ((DateTime.Now - proc.StartTime).TotalSeconds < 30) continue;
                    proc.Kill();
                }
                catch { }
            }

            // Wait for the killed processes to release the mutex
            _singleInstanceMutex.Dispose();
            for (var i = 0; i < 50; i++)
            {
                System.Threading.Thread.Sleep(200);
                _singleInstanceMutex = new Mutex(true, "VoiceGarden.UI.SingleInstance", out bool created);
                if (created)
                    return true;
            }
            // Someone else (a healthy instance) owns it now — defer to it.
            BringExistingInstanceToFront();
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool BringExistingInstanceToFront()
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
                return true;
            }
        }
        catch { }
        return false;
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
