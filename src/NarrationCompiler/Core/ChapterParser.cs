using System.Text.RegularExpressions;

namespace NarrationCompiler;

/// <summary>
/// Result of parsing a chapter markdown file.
/// </summary>
public record ParsedChapter(
    string FilePath,
    string ChapterTitle,
    string ProseContent
);

/// <summary>
/// Parses story chapter markdown files.
/// Extracts the chapter title (first line) and prose content (everything after # Content marker).
/// </summary>
public static class ChapterParser
{
    // Matches any heading level with just "Content" as text: # Content, ## Content, ### Content, etc.
    private static readonly Regex ContentMarkerRegex = new(@"^#{1,}\s+Content\s*$", RegexOptions.Compiled);

    // Matches the chapter title line: # Chapter NN: Title
    private static readonly Regex ChapterTitleRegex = new(@"^#\s+Chapter\s+.+$", RegexOptions.Compiled);

    /// <summary>
    /// Parse a chapter markdown file. Returns null and prints error if no Content marker found.
    /// </summary>
    public static ParsedChapter? Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"[ERROR] Chapter file not found: {filePath}");
            return null;
        }

        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
        {
            Console.Error.WriteLine($"[ERROR] Chapter file is empty: {filePath}");
            return null;
        }

        // Extract chapter title from first line
        string chapterTitle = "(untitled)";
        if (ChapterTitleRegex.IsMatch(lines[0]))
        {
            chapterTitle = lines[0].TrimStart('#', ' ');
        }

        // Find the # Content marker
        int contentLineIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (ContentMarkerRegex.IsMatch(lines[i]))
            {
                contentLineIndex = i;
                break;
            }
        }

        if (contentLineIndex == -1)
        {
            Console.Error.WriteLine($"[ERROR] No '# Content' marker found in: {filePath}");
            return null;
        }

        // Everything after the Content marker is prose
        var proseLines = lines.Skip(contentLineIndex + 1).ToArray();
        var prose = string.Join("\n", proseLines).Trim();

        if (string.IsNullOrWhiteSpace(prose))
        {
            Console.Error.WriteLine($"[ERROR] No prose content after '# Content' marker in: {filePath}");
            return null;
        }

        Console.WriteLine($"  [OK] {Path.GetFileName(filePath)} — \"{chapterTitle}\" ({prose.Length:N0} chars)");
        return new ParsedChapter(filePath, chapterTitle, prose);
    }
}