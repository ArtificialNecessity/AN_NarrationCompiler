using System.Reflection;
using System.Text.RegularExpressions;
using NarrationCompiler.Core;

namespace NarrationCompiler.Print;

/// <summary>
/// Implements the "publish" command: compiles chapters into a single HTML file.
/// </summary>
public static class PublishHtmlCommand
{
    public static int Execute(string chaptersDir, string? outputDir, int? throughChapter)
    {
        // 1. Resolve chapters directory
        chaptersDir = Path.GetFullPath(chaptersDir);
        if (!Directory.Exists(chaptersDir))
        {
            Console.Error.WriteLine($"[ERROR] Chapters directory not found: {chaptersDir}");
            return 1;
        }

        // 2. Look for .mirica.metadata.jsonc in the chapters dir
        var configPath = Path.Combine(chaptersDir, ".mirica.metadata.jsonc");
        BookConfig config;

        if (File.Exists(configPath))
        {
            var loaded = BookConfigLoader.Load(configPath);
            if (loaded == null) return 1;
            config = loaded;
        }
        else
        {
            // No config — use sensible defaults from the directory name
            Console.WriteLine($"[INFO] No .mirica.metadata.jsonc found, using defaults.");
            config = new BookConfig
            {
                Path = "./",
                Title = Path.GetFileName(chaptersDir),
                Ordering = "alpha"
            };
        }

        // 3. Discover and parse chapters
        Console.WriteLine($"\nScanning: {chaptersDir}");
        var chapterFiles = DiscoverChapters(chaptersDir, config);

        if (chapterFiles.Count == 0)
        {
            Console.Error.WriteLine("[ERROR] No chapter files found.");
            return 1;
        }

        // Apply --through-chapter filter
        if (throughChapter.HasValue)
        {
            chapterFiles = chapterFiles
                .Where(f => ParseChapterNumber(Path.GetFileName(f)) <= throughChapter.Value)
                .ToList();
            Console.WriteLine($"  Filtered to chapters through {throughChapter.Value} ({chapterFiles.Count} files)");
        }

        // 4. Parse all chapters
        Console.WriteLine($"  Found {chapterFiles.Count} chapter file(s)\n");
        var chapters = new List<ChapterData>();

        foreach (var file in chapterFiles)
        {
            var parsed = ParseChapter(file);
            if (parsed != null)
                chapters.Add(parsed);
        }

        if (chapters.Count == 0)
        {
            Console.Error.WriteLine("[ERROR] No chapters parsed successfully.");
            return 1;
        }

        var totalWords = chapters.Sum(c => c.WordCount);
        Console.WriteLine($"\n  Parsed {chapters.Count} chapters, {totalWords:N0} total words");

        // 5. Load CSS
        var css = LoadEmbeddedCss(config.Print?.Style ?? "serif-book");

        // 6. Render HTML
        Console.WriteLine("  Rendering HTML...");
        var html = HtmlRenderer.Render(config, chapters, css);

        // 7. Write output
        var resolvedOutputDir = outputDir ?? Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "RENDERED_OUTPUT");
        resolvedOutputDir = Path.GetFullPath(resolvedOutputDir);
        Directory.CreateDirectory(resolvedOutputDir);

        var safeTitle = Regex.Replace(config.Title, @"[^\w\-]", "_");
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
        var buildNumber = GetNextBuildNumber(resolvedOutputDir, safeTitle, dateStr);
        var outputFileName = $"{safeTitle}-{dateStr}-v{buildNumber}.html";
        var outputPath = Path.Combine(resolvedOutputDir, outputFileName);

        File.WriteAllText(outputPath, html);

        Console.WriteLine($"\n[DONE] {outputPath}");
        Console.WriteLine($"       {chapters.Count} chapters | {totalWords:N0} words | {new FileInfo(outputPath).Length / 1024:N0} KB");

        return 0;
    }

    // ─── Chapter Discovery ─────────────────────────────────────────────────

    private static List<string> DiscoverChapters(string chaptersDir, BookConfig config)
    {
        // If explicit chapters list exists, use it
        if (config.Chapters != null && config.Chapters.Count > 0)
        {
            var files = new List<string>();
            foreach (var entry in config.Chapters)
            {
                if (entry.SkipPrint == true) continue;
                var fullPath = Path.Combine(chaptersDir, entry.File);
                if (File.Exists(fullPath))
                    files.Add(fullPath);
                else
                    Console.Error.WriteLine($"  [WARN] Chapter file not found: {entry.File}");
            }
            return files;
        }

        // Auto-discover: find all Chapter_*.md files
        var pattern = "Chapter_*.md";
        var discovered = Directory.GetFiles(chaptersDir, pattern)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return discovered;
    }

    // ─── Chapter Parsing ───────────────────────────────────────────────────

    private static readonly Regex ContentMarkerRegex = new(@"^#{1,}\s+Content\s*$", RegexOptions.Compiled);
    private static readonly Regex ChapterTitleRegex = new(@"^#\s+Chapter\s+.+$", RegexOptions.Compiled);

    private static ChapterData? ParseChapter(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
        {
            Console.Error.WriteLine($"  [SKIP] Empty file: {Path.GetFileName(filePath)}");
            return null;
        }

        // Extract title
        string title = Path.GetFileNameWithoutExtension(filePath);
        for (int i = 0; i < Math.Min(5, lines.Length); i++)
        {
            if (ChapterTitleRegex.IsMatch(lines[i]))
            {
                title = lines[i].TrimStart('#', ' ');
                break;
            }
        }

        // Find content marker
        int contentIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (ContentMarkerRegex.IsMatch(lines[i]))
            {
                contentIndex = i;
                break;
            }
        }

        if (contentIndex == -1)
        {
            Console.Error.WriteLine($"  [SKIP] No '# Content' marker: {Path.GetFileName(filePath)}");
            return null;
        }

        // Everything after content marker is prose
        var prose = string.Join("\n", lines.Skip(contentIndex + 1)).Trim();
        if (string.IsNullOrWhiteSpace(prose))
        {
            Console.Error.WriteLine($"  [SKIP] No content after marker: {Path.GetFileName(filePath)}");
            return null;
        }

        var wordCount = CountWords(prose);
        var anchor = CreateAnchorId(title);

        Console.WriteLine($"  [OK] {Path.GetFileName(filePath)} — \"{title}\" ({wordCount:N0} words)");

        return new ChapterData
        {
            FilePath = filePath,
            Title = title,
            ProseContent = prose,
            WordCount = wordCount,
            SortKey = Path.GetFileName(filePath),
            AnchorId = anchor
        };
    }

    // ─── Utilities ────────────────────────────────────────────────────────

    private static int CountWords(string text)
    {
        // Remove markdown formatting before counting
        text = Regex.Replace(text, @"^#{1,6}\s+", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\*+([^*]+)\*+", "$1");
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string CreateAnchorId(string title)
    {
        var anchor = title.ToLowerInvariant();
        anchor = Regex.Replace(anchor, @"[^a-z0-9\s-]", "");
        anchor = Regex.Replace(anchor, @"\s+", "-");
        return anchor;
    }

    private static int ParseChapterNumber(string filename)
    {
        var match = Regex.Match(filename, @"Chapter_(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 999;
    }

    private static int GetNextBuildNumber(string outputDir, string baseName, string dateStr)
    {
        int build = 1;
        while (File.Exists(Path.Combine(outputDir, $"{baseName}-{dateStr}-v{build}.html")))
            build++;
        return build;
    }

    private static string LoadEmbeddedCss(string styleName)
    {
        // Load from the Styles directory next to the assembly
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var cssPath = Path.Combine(assemblyDir, "Styles", $"{styleName}.css");

        if (File.Exists(cssPath))
            return File.ReadAllText(cssPath);

        // Fallback: try relative to working directory
        var fallbackPath = Path.Combine("src", "NarrationCompiler", "Print", "Styles", $"{styleName}.css");
        if (File.Exists(fallbackPath))
            return File.ReadAllText(fallbackPath);

        // Last resort: look relative to the project structure
        var projectRoot = FindProjectRoot();
        if (projectRoot != null)
        {
            var projectCssPath = Path.Combine(projectRoot, "src", "NarrationCompiler", "Print", "Styles", $"{styleName}.css");
            if (File.Exists(projectCssPath))
                return File.ReadAllText(projectCssPath);
        }

        Console.Error.WriteLine($"[WARN] CSS style '{styleName}' not found, using minimal fallback.");
        return "body { font-family: Georgia, serif; max-width: 700px; margin: 0 auto; padding: 2rem; line-height: 1.8; }";
    }

    private static string? FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "NarrationCompiler.slnx")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }
}