namespace Mirica.Desktop.Providers.TextToSpeech.CartesiaProvider;

/// <summary>
/// Minimal shim replacing the full DebugSplatter from Mirica.Desktop.
/// Routes debug/info messages to Console.Error so they don't pollute stdout.
/// </summary>
internal static class DebugSplatter
{
    internal static void Info(string tag, string message)
        => Console.Error.WriteLine($"[{tag}] {message}");

    internal static void Debug(string tag, string message)
        => Console.Error.WriteLine($"[{tag}] {message}");
}