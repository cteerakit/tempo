using System.IO;

namespace Tempo.App;

/// <summary>Minimal append-only log at %APPDATA%\Tempo\tempo.log.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string path)
    {
        _path = path;
        try
        {
            // Keep the log from growing without bound across sessions.
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        if (_path is null)
            return;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }
    }
}
