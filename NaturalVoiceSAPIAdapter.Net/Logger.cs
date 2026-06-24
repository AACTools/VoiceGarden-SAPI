using System;
using System.IO;
using System.Text;

namespace NaturalVoiceSAPIAdapter;

internal static class Logger
{
    private static readonly object _lock = new();
    private static string? _logPath;
    private static bool _initialized;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(localAppData, "NaturalVoiceSAPIAdapter");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, "dotnet-adapter.log.txt");
        }
        catch
        {
            _logPath = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");

        EnsureInitialized();
        if (_logPath == null) return;

        try
        {
            lock (_lock)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
        }
        catch { }
    }
}
