using System.Text.Json.Serialization;

namespace NarrationCompiler.Core;

/// <summary>
/// A single entry in the .mirica.metadata.jsonc array.
/// Contains the base metadata fields plus optional compile-mode extensions.
/// </summary>
public class BookConfig
{
    // ─── Base metadata fields (standard .mirica.metadata.jsonc) ───────────────

    [JsonPropertyName("path")]
    public string Path { get; set; } = "./";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "narrative/chapters/prose";

    [JsonPropertyName("ordering")]
    public string Ordering { get; set; } = "alpha";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Untitled";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("current_stream")]
    public string? CurrentStream { get; set; }

    // ─── Extension: explicit chapter manifest ────────────────────────────────

    [JsonPropertyName("chapters")]
    public List<ChapterEntry>? Chapters { get; set; }

    // ─── Extension: print compilation settings ───────────────────────────────

    [JsonPropertyName("print")]
    public PrintConfig? Print { get; set; }

    // ─── Extension: audio compilation settings ───────────────────────────────

    [JsonPropertyName("audio")]
    public AudioConfig? Audio { get; set; }
}

/// <summary>
/// A single chapter entry in the explicit manifest.
/// </summary>
public class ChapterEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    [JsonPropertyName("skip_print")]
    public bool? SkipPrint { get; set; }

    [JsonPropertyName("skip_audio")]
    public bool? SkipAudio { get; set; }

    [JsonPropertyName("title_override")]
    public string? TitleOverride { get; set; }
}

/// <summary>
/// Print mode configuration.
/// </summary>
public class PrintConfig
{
    [JsonPropertyName("output_directory")]
    public string OutputDirectory { get; set; } = "./OUTPUT";

    [JsonPropertyName("style")]
    public string Style { get; set; } = "serif-book";

    [JsonPropertyName("scene_break")]
    public string SceneBreak { get; set; } = "* * *";

    [JsonPropertyName("omit_first_scene_break")]
    public bool OmitFirstSceneBreak { get; set; } = true;

    [JsonPropertyName("include_word_counts")]
    public bool IncludeWordCounts { get; set; } = true;

    [JsonPropertyName("include_toc")]
    public bool IncludeToc { get; set; } = true;
}

/// <summary>
/// Audio mode configuration.
/// </summary>
public class AudioConfig
{
    [JsonPropertyName("output_directory")]
    public string OutputDirectory { get; set; } = "./OUTPUT_AUDIO";

    [JsonPropertyName("voice_mapping")]
    public Dictionary<string, VoiceEntry>? VoiceMapping { get; set; }

    [JsonPropertyName("default_voice")]
    public string? DefaultVoice { get; set; }

    [JsonPropertyName("silence_gap_ms")]
    public int SilenceGapMs { get; set; } = 2000;

    [JsonPropertyName("output_format")]
    public string OutputFormat { get; set; } = "mp3";

    [JsonPropertyName("compression_bitrate_kbps")]
    public int CompressionBitrateKbps { get; set; } = 128;
}

/// <summary>
/// A voice mapping entry.
/// </summary>
public class VoiceEntry
{
    [JsonPropertyName("cartesia_voice_id")]
    public string? CartesiaVoiceId { get; set; }
}