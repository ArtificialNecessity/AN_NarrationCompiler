// NarrationCompiler - Story compiler: audio (TTS), print (HTML), and epub
// Entry point: command line parsing and dispatch

using AstroCryptKit;
using NarrationCompiler;
using NarrationCompiler.Print;

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

    case "publish":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("ERROR: publish requires a chapters directory path.");
            Console.Error.WriteLine("Usage: NarrationCompiler publish <chapters-dir> [--output-dir <dir>] [--through-chapter <N>]");
            return 1;
        }
        string? publishOutputDir = null;
        int? throughChapter = null;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--output-dir")
                publishOutputDir = args[i + 1];
            if (args[i] == "--through-chapter" && int.TryParse(args[i + 1], out var tc))
                throughChapter = tc;
        }
        // Default output to RENDERED_OUTPUT in project root
        if (publishOutputDir == null)
        {
            // Walk up from the exe to find the solution root (contains .slnx)
            var root = FindSolutionRoot(AppContext.BaseDirectory);
            publishOutputDir = root != null
                ? Path.Combine(root, "RENDERED_OUTPUT")
                : Path.Combine(Directory.GetCurrentDirectory(), "RENDERED_OUTPUT");
        }
        return PublishHtmlCommand.Execute(args[1], publishOutputDir, throughChapter);

    default:
        Console.Error.WriteLine($"ERROR: Unknown command '{command}'");
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("NarrationCompiler - Story compiler (audio, print, epub)");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  publish <chapters-dir> [--output-dir <dir>] [--through-chapter <N>]");
    Console.WriteLine("                                               Compile chapters to HTML");
    Console.WriteLine("  render-one <chapter.md> [--voice-id <id>]   Render a single chapter to audio");
    Console.WriteLine("  render <chapters-dir> [--output-dir <dir>]  Render all chapters to audio");
    Console.WriteLine("  init <chapters-dir> [--output <config-path>] Create a new config");
}

static string? FindSolutionRoot(string startDir)
{
    var dir = startDir;
    for (int i = 0; i < 10; i++)
    {
        if (Directory.GetFiles(dir, "*.slnx").Length > 0 || Directory.GetFiles(dir, "*.sln").Length > 0)
            return dir;
        var parent = Directory.GetParent(dir);
        if (parent == null) break;
        dir = parent.FullName;
    }
    return null;
}