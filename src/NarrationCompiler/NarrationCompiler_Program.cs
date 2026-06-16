// NarrationCompiler - Renders markdown story chapters into voiced audiobook
// Entry point: command line parsing and dispatch

using AstroCryptKit;
using NarrationCompiler;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();

// --- Unlock keystore FIRST for any command that needs it ---
AstroCryptKeystore? keystore = null;
if (command == "render-one" || command == "render")
{
    keystore = KeystoreLoader.LoadAndUnlock();
    if (keystore == null)
        return 1;
}

switch (command)
{
    case "render-one":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("ERROR: render-one requires a chapter file path.");
            Console.Error.WriteLine("Usage: NarrationCompiler render-one <chapter.md> [--voice-id <id>] [--provider <name>]");
            return 1;
        }
        var chapterPath = args[1];
        string? voiceIdOverride = null;
        string? outputDir = null;
        string? providerName = null;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--voice-id")
                voiceIdOverride = args[i + 1];
            if (args[i] == "--output-dir")
                outputDir = args[i + 1];
            if (args[i] == "--provider")
                providerName = args[i + 1];
        }
        return await RenderOneCommand.ExecuteAsync(keystore!, chapterPath, voiceIdOverride, outputDir, providerName);

    case "render":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("ERROR: render requires a chapters directory path.");
            Console.Error.WriteLine("Usage: NarrationCompiler render <chapters-dir> [--output-dir <dir>]");
            return 1;
        }
        // TODO: Phase 3 - implement full pipeline
        Console.WriteLine($"[render] Chapters dir: {args[1]}");
        return 0;

    case "init":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("ERROR: init requires a chapters directory path.");
            Console.Error.WriteLine("Usage: NarrationCompiler init <chapters-dir> [--output <config-path>]");
            return 1;
        }
        // TODO: Phase 6 - implement init
        Console.WriteLine($"[init] Chapters dir: {args[1]}");
        return 0;

    default:
        Console.Error.WriteLine($"ERROR: Unknown command '{command}'");
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("NarrationCompiler - Markdown story chapter to audiobook renderer");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  render-one <chapter.md> [--voice-id <id>]    Render a single chapter");
    Console.WriteLine("  render <chapters-dir> [--output-dir <dir>]   Render all chapters in directory");
    Console.WriteLine("  init <chapters-dir> [--output <config-path>] Create a new render config");
}