using System.Diagnostics;

namespace GamingLiveTranslator.Utilities;

/// <summary>
/// Minimal diagnostic logger for runtime tracking and debugging.
/// </summary>
public static class Logger
{
    private static readonly object _syncLock = new();

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message} | Exception: {ex}" : message;
        Log("ERROR", fullMessage);
    }

    private static void Log(string level, string message)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        lock (_syncLock)
        {
            Debug.WriteLine(formatted);
            Console.WriteLine(formatted);
        }
    }
}
