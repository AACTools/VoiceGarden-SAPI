using Avalonia;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VoiceGarden.UI;

internal class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

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

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
