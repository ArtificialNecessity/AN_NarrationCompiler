namespace NarrationCompiler.Core;

/// <summary>
/// Rich chapter data produced by parsing + processing.
/// Used by both Print and Audio modes.
/// </summary>
public class ChapterData
{
    /// <summary>Source file path.</summary>
    public required string FilePath { get; init; }

    /// <summary>Chapter title extracted from the markdown (e.g. "Chapter 01: Deep Ocean Vents").</summary>
    public required string Title { get; init; }

    /// <summary>Raw prose content (everything after # Content marker).</summary>
    public required string ProseContent { get; init; }

    /// <summary>Word count of the prose content.</summary>
    public int WordCount { get; init; }

    /// <summary>Sort key for ordering (parsed from filename).</summary>
    public required string SortKey { get; init; }

    /// <summary>HTML-safe anchor ID for TOC linking.</summary>
    public required string AnchorId { get; init; }
}