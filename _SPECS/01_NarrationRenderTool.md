# Narration Render Tool — Implementation Spec

## Overview

A .NET console tool that renders markdown story chapters into a voiced audiobook using Cartesia TTS.

**Input:** A `.mirica.metadata.jsonc` config file (see [02_NarrativeCompilerMetadataSpec.md](./02_NarrativeCompilerMetadataSpec.md) for full format).
**Output:** A single concatenated and compressed audio file (mp3/ogg).

---

## Config Format

The audio render config lives in the `"audio"` section of a `.mirica.metadata.jsonc` entry.
See **[02_NarrativeCompilerMetadataSpec.md](./02_NarrativeCompilerMetadataSpec.md)** for the full config schema, chapter manifest format, and voice resolution logic.

---

## Key Decryption Flow

- Keystore path (platform-independent): `{LocalAppData}/ArtificialNecessity/MiricaLLMData/EncryptedKeys.json`
- Always reads `cartesia_api_key` for the API credential
- Reads `cartesia_voice_id` as fallback voice (only used if no voice resolved from config)
- User is prompted for their AstroCrypt password on the command line (masked input)
- Passphrase is validated via `AstroCryptKeystore.ValidatePassphrase()` before proceeding

---

## Audio Caching / Skip Logic

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

- [ ] Parse the `.mirica.metadata.jsonc` config (System.Text.Json with AllowComments)
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

- [ ] `--init <chapters-dir>` command: scans the directory, creates a `.mirica.metadata.jsonc` with:
  - [ ] Base metadata fields populated
  - [ ] `chapters` array pre-populated with all `Chapter_*.md` files found
  - [ ] Sensible `audio` defaults (`silence_gap_ms: 2000`, `output_format: "mp3"`, `compression_bitrate_kbps: 128`)
  - [ ] Empty `voice_mapping` and `default_voice: null` (uses keystore fallback)
- [ ] Auto-chapter discovery logic in the render pipeline (scan, diff, append, print, persist)

---

## Command Line Interface (target)

```
# Phase 2 — minimal single chapter:
NarrationCompiler render-one <chapter.md> [--voice-id <id>]

# Phase 3+ — full audio pipeline:
NarrationCompiler render <config.jsonc> [--auto-chapters]

# Print compilation (HTML output):
NarrationCompiler publish <config.jsonc> [--through-chapter <N>]

# Phase 6 — init:
NarrationCompiler init <chapters-dir> [--output <config-path>]
```