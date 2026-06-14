# Narration Render Tool — Implementation Spec

## Overview

A .NET console tool that renders markdown story chapters into a voiced audiobook using Cartesia TTS.

**Input:** A JSON render config pointing at chapter files, voice mappings, and output settings.
**Output:** A single concatenated and compressed audio file (mp3/ogg).

---

## JSON Render Config Shape

```json
{
  "voice_mapping": {
    "narrator": { "cartesia_voice_id": "a1b2c3d4-..." },
    "suzy":     { "cartesia_voice_id": "e5f6g7h8-..." }
  },
  "default_voice": "narrator",
  "chapters": [
    { "file": "./chapters/Chapter_01_Seven_Minutes.md" },
    { "file": "./chapters/Chapter_02_The_Girl_On_The_Wall.md", "voice": "suzy" }
  ],
  "output_dir": "./output",
  "silence_gap_ms": 2000,
  "output_format": "mp3",
  "compression_bitrate_kbps": 128
}
```

**`auto_chapters` option:**
- If `"auto_chapters": true` is set, the tool scans `"chapters_dir"` (or the directory of the first chapter file) for markdown files matching `Chapter_*.md`
- Any files found that are NOT already in the `"chapters"` array are **appended** to it automatically
- The tool prints a clear message for each auto-discovered chapter: `[AUTO] Added: Chapter_05_Three_In_A_Kitchen.md`
- The updated chapter list is written back to the config JSON so subsequent runs have an explicit index

**Voice resolution order (per chapter):**
1. Chapter-level `"voice"` field → lookup in `voice_mapping`
2. Top-level `"default_voice"` → lookup in `voice_mapping`
3. Fallback: `cartesia_voice_id` from the global `EncryptedKeys.json` keystore

---

## Key Decryption Flow

- Keystore path (platform-independent): `{LocalAppData}/ArtificialNecessity/MiricaLLMData/EncryptedKeys.json`
- Always reads `cartesia_api_key` for the API credential
- Reads `cartesia_voice_id` as fallback voice (only used if no voice resolved from config)
- User is prompted for their AstroCrypt password on the command line (masked input)
- Passphrase is validated via `AstroCryptKeystore.ValidatePassphrase()` before proceeding

---

## Chapter Parsing Rules

- Find the line matching regex: `^#{1,}\s+Content\s*$` (any H-level with just "Content")
- Everything **after** that line is prose to be rendered
- The `# Chapter NN: Title` line (first line) is extracted for metadata/logging
- Everything between the chapter title and `# Content` is **ignored** (metadata sections)
- If no `# Content` marker found → print error to console, skip the file, continue

---

## Caching / Skip Logic

- For each chapter: compute hash of `(prose_text + resolved_voice_id)`
- Per-chapter raw audio files stored in `output_dir` as `chapter_NN_HASH.wav` (or `.raw`)
- If a matching file already exists → skip rendering, print "cached" status
- This allows re-running after adding new chapters without re-rendering existing ones

---

## Final Assembly

- Concatenate all per-chapter PCM audio in order
- Insert silence gap (configurable `silence_gap_ms`) between chapters
- Write intermediate uncompressed WAV
- Compress to target format (mp3/ogg) at specified bitrate using a NuGet audio library
- Print final output file path

---

## Dependencies

- **AstroCryptKit** (copied into solution) — keystore decryption + password prompt
- **CartesiaTTSProvider** (adapted from Mirica.Desktop) — WebSocket streaming TTS
- **NAudio** or **FFMpegCore** (NuGet) — WAV assembly + lossy compression

---

## Phased Implementation Plan

### Phase 1 — Project Scaffold & Code Copy

- [ ] Create .NET 9 console app project (`src/NarrationCompiler/NarrationCompiler.csproj`)
- [ ] Copy `AstroCrypt.cs` and `AstroCryptKeystore.cs` into `Crypto/` folder (adjust namespace)
- [ ] Copy and adapt `CartesiaTTSProvider.cs` + `ITTSProvider.cs` into `TTS/` folder
  - [ ] Remove `DebugSplatter` dependency (replace with `Console.WriteLine`)
  - [ ] Remove `FluidUI` dependency
  - [ ] Keep the WebSocket streaming logic intact
- [ ] Verify project builds cleanly

### Phase 2 — Minimal Single-Chapter CLI Render

- [ ] Accept command-line args: `<chapter-file-path>` and `<voice-id>` (hardcoded/direct)
- [ ] Prompt for AstroCrypt password, load keystore, extract `cartesia_api_key`
- [ ] Parse the chapter markdown file (extract prose after `# Content`)
- [ ] Send prose text to Cartesia via WebSocket session
- [ ] Collect PCM chunks → write raw PCM to a `.wav` file
- [ ] Print success/path — confirm end-to-end audio generation works

### Phase 3 — JSON Config & Multi-Chapter Pipeline

- [ ] Define and parse the JSON render config (System.Text.Json)
- [ ] Implement voice resolution logic (chapter → default → keystore fallback)
- [ ] Iterate all chapters: parse, resolve voice, print status
- [ ] Implement content hashing + cache-check (skip already-rendered chapters)
- [ ] Render all un-cached chapters sequentially

### Phase 4 — Audio Assembly & Compression

- [ ] Add NuGet package for audio processing (NAudio or FFMpegCore)
- [ ] Concatenate per-chapter WAV files with silence gaps
- [ ] Write combined uncompressed WAV
- [ ] Compress to mp3/ogg at configured bitrate
- [ ] Print final output path

### Phase 5 — Polish & Error Handling

- [ ] Graceful error handling (network failures, bad files, wrong password)
- [ ] Progress reporting (chapter N of M, estimated time)
- [ ] Retry logic for transient WebSocket failures
- [ ] Clean up temp files on success
- [ ] README with usage instructions

### Phase 6 — Init & Auto-Chapters

- [ ] `--init <chapters-dir>` command: scans the directory, creates a `NarrationRenderConfig.json` with:
  - [ ] `auto_chapters: true`
  - [ ] `chapters_dir` set to the provided path
  - [ ] `chapters` array pre-populated with all `Chapter_*.md` files found
  - [ ] Sensible defaults (`silence_gap_ms: 2000`, `output_format: "mp3"`, `compression_bitrate_kbps: 128`)
  - [ ] Empty `voice_mapping` and `default_voice: null` (uses keystore fallback)
- [ ] Auto-chapter discovery logic in the render pipeline (scan, diff, append, print, persist)

---

## Command Line Interface (target)

```
# Phase 2 — minimal single chapter:
NarrationCompiler render-one <chapter.md> [--voice-id <id>]

# Phase 3+ — full pipeline:
NarrationCompiler render <config.json> [--auto-chapters]

# Phase 6 — init:
NarrationCompiler init <chapters-dir> [--output <config-path>]
```