using System.IO;
using System.Text;

namespace OsuRoam.Services;

public static class AppLog
{
    private static readonly object Sync = new();
    private static string? currentPath;
    public static event Action<string>? EntryAdded;
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam", "Logs");
    public static string CurrentPath { get { lock (Sync) { currentPath ??= createPath(); return currentPath; } } }
    public static void Info(string message) => write("INFO", message, null);
    public static void Error(string message, Exception? exception = null) => write("ERROR", message, exception);
    private static void write(string level, string message, Exception? exception)
    {
        string entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] [{level}] {message}" + (exception is null ? string.Empty : Environment.NewLine + exception) + Environment.NewLine;
        try { lock (Sync) { File.AppendAllText(CurrentPath, entry, new UTF8Encoding(false)); } } catch { }
        EntryAdded?.Invoke(entry.TrimEnd());
    }
    private static string createPath()
    {
        Directory.CreateDirectory(LogDirectory);
        return Path.Combine(LogDirectory, $"osu-roam-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }
}
