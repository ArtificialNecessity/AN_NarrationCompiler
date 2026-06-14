// NarrationCompiler - Renders markdown story chapters into voiced audiobook
// Entry point: command line parsing and dispatch

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "render-one":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("ERROR: render-one requires a chapter file path.");
            Console.Error.WriteLine("Usage: NarrationCompiler render-one <chapter.md> [--voice-id <id>]");
            return 1;
        }
        var chapterPath = args[1];
        string? voiceIdOverride = null;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--voice-id")
                voiceIdOverride = args[i + 1];
        }
        // TODO: Phase 2 - implement single chapter render
        Console.WriteLine($"[render-one] Chapter: {chapterPath}");
        Console.WriteLine($"[render-one] Voice override: {voiceIdOverride ?? "(keystore fallback)"}");
        return 0;

    case "render":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("ERROR: render requires a config JSON path.");
            Console.Error.WriteLine("Usage: NarrationCompiler render <config.json> [--auto-chapters]");
            return 1;
        }
        // TODO: Phase 3 - implement full pipeline
        Console.WriteLine($"[render] Config: {args[1]}");
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
    Console.WriteLine("  render-one <chapter.md> [--voice-id <id>]   Render a single chapter");
    Console.WriteLine("  render <config.json> [--auto-chapters]      Render full audiobook from config");
    Console.WriteLine("  init <chapters-dir> [--output <config-path>] Create a new render config");
}