using System.Text;
using System.Text.RegularExpressions;
using NarrationCompiler.Core;

namespace NarrationCompiler.Print;

/// <summary>
/// Renders a list of ChapterData into a single self-contained HTML file.
/// Embeds CSS inline for email/portability.
/// </summary>
public static class HtmlRenderer
{
    /// <summary>
    /// Render the full HTML document.
    /// </summary>
    public static string Render(BookConfig config, List<ChapterData> chapters, string css)
    {
        var printConfig = config.Print ?? new PrintConfig();
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{HtmlEncode(config.Title)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine(css);
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Title page
        RenderTitlePage(sb, config);

        // Table of contents
        if (printConfig.IncludeToc)
            RenderToc(sb, chapters, printConfig);

        // Chapters
        for (int i = 0; i < chapters.Count; i++)
        {
            RenderChapter(sb, chapters[i], printConfig);
            if (i < chapters.Count - 1)
                sb.AppendLine("<hr>");
        }

        // Footer
        sb.AppendLine($"<div class=\"footer\">Generated {DateTime.Now:yyyy-MM-dd} — {chapters.Count} chapters, {chapters.Sum(c => c.WordCount):N0} words</div>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static void RenderTitlePage(StringBuilder sb, BookConfig config)
    {
        sb.AppendLine("<div class=\"title-page\">");
        sb.AppendLine($"  <h1>{HtmlEncode(config.Title)}</h1>");
        if (!string.IsNullOrEmpty(config.Description))
            sb.AppendLine($"  <p class=\"description\">{HtmlEncode(config.Description)}</p>");
        sb.AppendLine("</div>");
    }

    private static void RenderToc(StringBuilder sb, List<ChapterData> chapters, PrintConfig config)
    {
        sb.AppendLine("<div class=\"toc\">");
        sb.AppendLine("  <h2>Contents</h2>");

        if (config.IncludeWordCounts)
        {
            var totalWords = chapters.Sum(c => c.WordCount);
            sb.AppendLine($"  <p class=\"total-words\">{totalWords:N0} words</p>");
        }

        sb.AppendLine("  <ul>");
        foreach (var chapter in chapters)
        {
            sb.Append($"    <li><a href=\"#{chapter.AnchorId}\">{HtmlEncode(chapter.Title)}</a>");
            if (config.IncludeWordCounts)
                sb.Append($"<span class=\"word-count\">({chapter.WordCount:N0})</span>");
            sb.AppendLine("</li>");
        }
        sb.AppendLine("  </ul>");
        sb.AppendLine("</div>");
    }

    private static void RenderChapter(StringBuilder sb, ChapterData chapter, PrintConfig config)
    {
        sb.AppendLine($"<div class=\"chapter\" id=\"{chapter.AnchorId}\">");
        sb.AppendLine($"  <h2>{HtmlEncode(chapter.Title)}</h2>");

        // Process prose content into HTML paragraphs
        var html = ProseToHtml(chapter.ProseContent, config);
        sb.AppendLine(html);

        sb.AppendLine("</div>");
    }

    /// <summary>
    /// Convert markdown prose to HTML paragraphs.
    /// Handles: paragraphs, scene breaks (### headings), bold, italic.
    /// </summary>
    private static string ProseToHtml(string prose, PrintConfig config)
    {
        var sb = new StringBuilder();
        var lines = prose.Split('\n');
        var currentParagraph = new StringBuilder();
        bool isFirstSceneBreak = true;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Scene break: ### heading
            if (trimmed.StartsWith("###"))
            {
                // Flush current paragraph
                FlushParagraph(sb, currentParagraph);

                if (isFirstSceneBreak && config.OmitFirstSceneBreak)
                {
                    isFirstSceneBreak = false;
                    continue;
                }
                isFirstSceneBreak = false;

                sb.AppendLine($"  <div class=\"scene-break\">{HtmlEncode(config.SceneBreak)}</div>");
                continue;
            }

            // Horizontal rule / section break
            if (trimmed == "---" || trimmed == "***" || trimmed == "___")
            {
                FlushParagraph(sb, currentParagraph);
                sb.AppendLine($"  <div class=\"scene-break\">{HtmlEncode(config.SceneBreak)}</div>");
                continue;
            }

            // Empty line = paragraph break
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph(sb, currentParagraph);
                continue;
            }

            // Accumulate paragraph text
            if (currentParagraph.Length > 0)
                currentParagraph.Append(' ');
            currentParagraph.Append(trimmed);
        }

        // Flush final paragraph
        FlushParagraph(sb, currentParagraph);

        return sb.ToString();
    }

    private static void FlushParagraph(StringBuilder sb, StringBuilder paragraph)
    {
        if (paragraph.Length == 0) return;

        var text = paragraph.ToString();
        paragraph.Clear();

        // Apply inline markdown formatting
        text = ApplyInlineFormatting(text);

        sb.AppendLine($"  <p>{text}</p>");
    }

    /// <summary>
    /// Apply basic inline markdown: **bold**, *italic*, and ***bold italic***.
    /// </summary>
    private static string ApplyInlineFormatting(string text)
    {
        // Bold italic: ***text*** or ___text___
        text = Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "<strong><em>$1</em></strong>");
        // Bold: **text**
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        // Italic: *text*
        text = Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<em>$1</em>");
        // Em dash normalization
        text = text.Replace(" -- ", " — ").Replace("--", "—");

        return text;
    }

    private static string HtmlEncode(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}