# Narrative Compiler — Metadata & Config Spec

## Overview

This document defines the unified configuration format used by NarrationCompiler for all compile modes (audio, print, and eventually EPUB). The format extends the `.mirica.metadata.jsonc` array convention — each entry in the array describes a content source with its associated compilation settings.

---

## File Convention

- **Filename:** `.mirica.metadata.jsonc` (placed in or near the chapters directory)
- **Format:** JSONC (JSON with `//` line comments allowed)
- **Shape:** Top-level JSON **array** of entry objects
- **Parsing:** `System.Text.Json` with `JsonSerializerOptions.ReadCommentHandling = JsonCommentHandling.Skip`

---

## Base Entry Fields (required by convention)

These fields are defined by the `.mirica.metadata.jsonc` standard and MUST NOT be renamed, removed, or restructured:

| Field | Type | Description |
|-------|------|-------------|
| `path` | `string` | Relative path to the content directory (from the config file's location) |
| `type` | `string` | Content type identifier (e.g. `"narrative/chapters/prose"`) |
| `ordering` | `string` | How to sort discovered files: `"alpha"` = alphabetical by filename |
| `title` | `string` | Human-readable title of this content collection |
| `description` | `string` | Brief description of the content |
| `current_stream` | `string?` | Optional path to the current writing stream file |

---

## Extension: `chapters` Array (Explicit Manifest)

When present, `chapters` provides an explicit ordered list of chapter files to compile. When absent, the tool auto-discovers files from `path` using `ordering`.

```jsonc
"chapters": [
  { "file": "Chapter_01_Seven_Minutes.md" },
  { "file": "Chapter_02_The_Girl_On_The_Wall.md", "voice": "suzy" },
  { "file": "Chapter_03_Patrol.md", "skip_print": true },
  { "file": "Chapter_04_Kitchen.md", "title_override": "The Kitchen" }
]
```

### Chapter entry fields:

| Field | Type | Description |
|-------|------|-------------|
| `file` | `string` | **Required.** Filename relative to `path` |
| `voice` | `string?` | Audio mode: voice name (resolved via `audio.voice_mapping`) |
| `skip_print` | `bool?` | Print mode: exclude this chapter from print output |
| `skip_audio` | `bool?` | Audio mode: exclude this chapter from audio render |
| `title_override` | `string?` | Override the title extracted from the markdown file |

### Resolution logic:

1. If `"chapters"` array is present → use it as the ordered manifest (paths relative to `path`)
2. If `"chapters"` is absent → auto-discover `Chapter_*.md` files in `path`, sorted per `ordering`
3. Auto-discovery can be combined with an explicit list: files in the directory that are NOT in `chapters` are appended (when `auto_discover: true` is set alongside an explicit list)

---

## Extension: `print` Object

Configuration for HTML print compilation. Produces a single self-contained `.html` file with embedded CSS.

```jsonc
"print": {
  "output_directory": "./OUTPUT",
  "style": "serif-book",
  "scene_break": "* * *",
  "omit_first_scene_break": true,
  "include_word_counts": true,
  "include_toc": true
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `output_directory` | `string` | `"./OUTPUT"` | Where to write the compiled HTML file |
| `style` | `string` | `"serif-book"` | CSS theme: `"serif-book"`, `"sans-modern"`, `"minimal"` |
| `scene_break` | `string` | `"* * *"` | Text/HTML rendered for `###` scene headings |
| `omit_first_scene_break` | `bool` | `true` | Skip the first scene break in each chapter |
| `include_word_counts` | `bool` | `true` | Show per-chapter word counts in TOC |
| `include_toc` | `bool` | `true` | Generate a table of contents |

---

## Extension: `audio` Object

Configuration for TTS audio rendering. See `01_NarrationRenderTool.md` for full audio pipeline details.

```jsonc
"audio": {
  "output_directory": "./OUTPUT_AUDIO",
  "voice_mapping": {
    "narrator": { "cartesia_voice_id": "a1b2c3d4-..." },
    "suzy":     { "cartesia_voice_id": "e5f6g7h8-..." }
  },
  "default_voice": "narrator",
  "silence_gap_ms": 2000,
  "output_format": "mp3",
  "compression_bitrate_kbps": 128
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `output_directory` | `string` | `"./OUTPUT_AUDIO"` | Where to write rendered audio files |
| `voice_mapping` | `object` | `{}` | Named voices → Cartesia voice IDs |
| `default_voice` | `string?` | `null` | Default voice name (looked up in `voice_mapping`) |
| `silence_gap_ms` | `int` | `2000` | Silence between chapters in milliseconds |
| `output_format` | `string` | `"mp3"` | Final output format: `"mp3"` or `"ogg"` |
| `compression_bitrate_kbps` | `int` | `128` | Compression bitrate |

### Voice resolution order (per chapter):

1. Chapter-level `"voice"` field → lookup in `audio.voice_mapping`
2. `audio.default_voice` → lookup in `audio.voice_mapping`
3. Fallback: `cartesia_voice_id` from the global `EncryptedKeys.json` keystore

---

## Extension: `epub` Object (Future)

Reserved for full EPUB packaging (XHTML + CSS + audio + metadata). Will be specced when implemented.

---

## Full Example

```jsonc
[
  {
    // Base metadata (standard .mirica.metadata.jsonc fields)
    "path": "./",
    "type": "narrative/chapters/prose",
    "ordering": "alpha",
    "title": "Reina Winters at KMR",
    "description": "First-person present-tense prose chapters — Reina's parallel POV of the KMR timeline.",
    "current_stream": "../CURRENT_STREAM.md",

    // Explicit chapter manifest (optional — overrides auto-discovery)
    "chapters": [
      { "file": "Chapter_01_Seven_Minutes.md" },
      { "file": "Chapter_02_The_Girl_On_The_Wall.md", "voice": "suzy" },
      { "file": "Chapter_03_Patrol.md" },
      { "file": "Chapter_04_Kitchen.md" },
      { "file": "Chapter_05_Three_In_A_Kitchen.md", "skip_print": true }
    ],

    // Print compilation settings
    "print": {
      "output_directory": "./OUTPUT",
      "style": "serif-book",
      "scene_break": "* * *",
      "omit_first_scene_break": true,
      "include_word_counts": true,
      "include_toc": true
    },

    // Audio compilation settings
    "audio": {
      "output_directory": "./OUTPUT_AUDIO",
      "voice_mapping": {
        "narrator": { "cartesia_voice_id": "a1b2c3d4-0000-0000-0000-000000000000" },
        "suzy":     { "cartesia_voice_id": "e5f6g7h8-0000-0000-0000-000000000000" }
      },
      "default_voice": "narrator",
      "silence_gap_ms": 2000,
      "output_format": "mp3",
      "compression_bitrate_kbps": 128
    }
  }
]
```

---

## Chapter Parsing Rules

These apply to all compile modes (audio and print):

- Find the line matching regex: `^#{1,}\s+Content\s*$` (any H-level with just "Content")
- Everything **after** that line is prose/content to be rendered
- The `# Chapter NN: Title` line (first line) is extracted for metadata/logging
- Everything between the chapter title and `# Content` is **ignored** (metadata sections)
- If no `# Content` marker found → print error to console, skip the file, continue

---

## CLI Integration

The NarrationCompiler tool reads this config for all commands:

```
# Print compilation (HTML output):
NarrationCompiler publish <config.jsonc> [--through-chapter <N>]

# Audio render (full pipeline):
NarrationCompiler render <config.jsonc> [--auto-chapters]

# Audio render (single chapter, no config needed):
NarrationCompiler render-one <chapter.md> [--voice-id <id>]

# Initialize a new config:
NarrationCompiler init <chapters-dir> [--output <config-path>]
```

---

## Project Structure

```
src/NarrationCompiler/
├── NarrationCompiler_Program.cs      # Entry point, command dispatch
├── NarrationCompiler.csproj
│
├── Core/                             # Shared across all modes
│   ├── BookConfig.cs                 # Metadata model (deserialized from .jsonc)
│   ├── BookConfigLoader.cs           # JSONC loading + validation
│   ├── ChapterParser.cs              # Markdown → structured chapter data
│   ├── ChapterData.cs                # Rich chapter model (title, content, word count)
│   └── ContentProcessor.cs           # Scene break handling, prose formatting
│
├── Audio/                            # TTS/audiobook mode
│   ├── RenderOneCommand.cs
│   ├── RenderManifest.cs
│   └── TTS/
│       ├── CartesiaTTSProvider.cs
│       ├── ITTSProvider.cs
│       └── WavWriter.cs
│
├── Print/                            # HTML single-file output
│   ├── PublishHtmlCommand.cs         # CLI command handler
│   ├── HtmlRenderer.cs              # Chapter data → HTML document
│   └── Styles/
│       └── BookStyle.css             # Embedded at build time
│
├── Crypto/                           # Keystore decryption
│   ├── AstroCrypt.cs
│   └── AstroCryptKeystore.cs
│
└── Utils/
    ├── KeystoreLoader.cs
    └── DebugSplatter.cs
```