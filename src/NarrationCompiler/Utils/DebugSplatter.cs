namespace Mirica.Desktop.Providers.TextToSpeech.CartesiaProvider;

/// <summary>
/// Minimal shim replacing the full DebugSplatter from Mirica.Desktop.
/// Logs to a file in the ArtificialNecessity Logs directory.
/// </summary>
internal static class DebugSplatter
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArtificialNecessity", "Logs");

    private static readonly string LogFile = Path.Combine(LogDir, "NarrationCompiler.log");

    private static readonly object Lock = new();
    private static bool _initialized;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Lock)
        {
            if (_initialized) return;
            Directory.CreateDirectory(LogDir);
            _initialized = true;
        }
    }

    internal static void Info(string tag, string message)
    {
        EnsureInitialized();
        var line = $"{DateTime.Now:HH:mm:ss.fff} [INFO] [{tag}] {message}";
        lock (Lock) { File.AppendAllText(LogFile, line + Environment.NewLine); }
    }

    internal static void Debug(string tag, string message)
    {
        EnsureInitialized();
        var line = $"{DateTime.Now:HH:mm:ss.fff} [DBUG] [{tag}] {message}";
        lock (Lock) { File.AppendAllText(LogFile, line + Environment.NewLine); }
    }
}