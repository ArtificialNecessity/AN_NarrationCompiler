# AI Voice Performance — TTS Options Overview (June 2026)

**Focus:** Audiobook narration, voice performance, expressive delivery with text-markup direction.
**Last Updated:** 2026-06-16

## Section 1: Pay-As-You-Go (fal.ai / Replicate / Per-Generation APIs)

These charge per generation — no monthly commitment, no expiring credit buckets.

### fal.ai Models

Already integrated into AstroNMCL/DreamTide pipeline via C# HTTP (same pattern as Seedream image generation).

| Model | Cost | Inline Tags | Voice Cloning | Streaming | Multi-Speaker | Languages | Notes |
|-------|------|-------------|---------------|-----------|---------------|-----------|-------|
| **Orpheus TTS** | $0.05/1K chars | 8 emotive tags (`<excited>`, `<fearful>`, `<angry>`, `<sad>`, `<surprised>`, `<disgusted>`, `<happy>`, `<neutral>`) | No | Yes | No | English-focused | Llama-based Speech-LLM, good emotional expressiveness |
| **Dia TTS** (Nari Labs) | $0.04/1K chars | `(laughs)`, `(sighs)`, `(clears throat)`, `(coughs)` + speaker tags `[S1]`/`[S2]` | Yes (from sample audio) | No | Yes (2 speakers) | English only | 1.6B params, Apache 2.0, great for dialogue |
| **xAI TTS** | Per-use | Inline speech tags for delivery control | No | Yes | No | Multi | 5 expressive voices |
| **Gemini TTS** | Per-use | Natural-language prompting for style/pace/accent/emotion | No | No | Yes | 30+ voices | Google's model, good direction-following |
| **F5-TTS** | $0.05/1K chars | None | Yes (zero-shot from 1 sample) | No | No | Multi | Diffusion-based, cheap voice cloning |
| **ElevenLabs** (via fal) | Per-use (proxy) | Full v3 audio tags if using v3 model | Yes | HTTP only (v3) | Yes (v3) | 70+ | Just proxies to ElevenLabs API |

### Replicate Models

| Model | Cost | Inline Tags | Voice Cloning | Notes |
|-------|------|-------------|---------------|-------|
| **Fish Speech 1.5** | ~$0.01/run | Limited | Yes | **Outdated** — predates S2's inline tag system. NOT S2-Pro quality. |
| Various older TTS | ~$0.01-0.02/run | Varies | Varies | Community-uploaded, quality varies |

**Gap:** Fish Audio S2-Pro is NOT available on fal.ai or Replicate yet. Nor is Chatterbox Turbo or ElevenLabs v3 natively. The best expressive models are missing from aggregators.

### Direct Pay-Per-Use APIs (No Subscription Required)

| Provider | Cost | Inline Tags | Voice Cloning | Streaming | Quality Tier | Notes |
|----------|------|-------------|---------------|-----------|--------------|-------|
| **Fish Audio API** | $15/1M chars | Open-domain word-level `[tag]` system, 48+ emotion tags, 5 tone tags, 10 special tags | Yes (from ref audio) | Yes (sub-150ms TTFA) | Top tier | See subscription note below — free tier is 7 min, then subscription kicks in |
| **Cartesia Sonic 3.5** | Per-use | Limited | Yes | Yes (40ms TTFA) | High | Latency king, SSM architecture |
| **Deepgram Aura-2** | Per-use | Limited | No | Yes (<90ms) | Good | Low-latency focused |

---

## Section 2: Local / Self-Hosted (Free After Hardware)

No ongoing cost. Apache 2.0 or MIT licensed. Run on your own GPU.

### Installation Viability on Current Hardware (RTX 4080 16GB, Windows/WSL2)

| Model | VRAM Needed | Fits 4080 16GB? | Install Complexity | Compile Speedup? |
|-------|-------------|-----------------|--------------------|--------------------|
| **Fish Audio S2-Pro** (bf16) | ~17 GB peak | Tight — may OOM on long generations | Medium (conda + pip, WSL2 recommended) | Yes (`torch.compile`, Linux/WSL2 only) |
| **Fish Audio S2-Pro** (INT8 quant) | ~5.1 GB | **Yes, easily** | Medium (same + download INT8 checkpoint) | Yes |
| **Chatterbox Turbo** | ~6.8 GB | **Yes** | Easy (pip install) | N/A (1-step diffusion) |
| **Chatterbox Original** | ~8 GB | **Yes** | Easy | N/A |
| **Chatterbox Multilingual** | ~8 GB | **Yes** | Easy | N/A |
| **Dia / Dia2** (Nari Labs) | ~6 GB | **Yes** | Easy (pip install) | No |
| **Kokoro** | ~0.3 GB | **Yes (runs on CPU)** | Trivial | N/A |
| **VibeVoice 7B** | ~19 GB | **No** — needs 24GB+ | Medium | Limited |
| **VibeVoice 1.5B** | ~6 GB | **Yes** | Medium | Limited |
| **CosyVoice 2** | ~4 GB | **Yes** | Medium | Yes |
| **IndexTTS-2** | ~8 GB | **Yes** | Medium | Limited |

### Feature Comparison — Local Models

| Model | Inline Performance Tags | Voice Cloning | Emotion Control | Multi-Speaker | Streaming | Languages | License |
|-------|------------------------|---------------|-----------------|---------------|-----------|-----------|---------|
| **Fish Audio S2-Pro** | **Open-domain word-level** — `[whisper]`, `[voice breaking]`, `[sarcastic]`, `[professional broadcast tone]`, free-form descriptions | Yes (ref audio) | Via inline tags (open-ended) | Up to 8 speakers | Yes (SGLang) | 80+ | Apache 2.0 |
| **Chatterbox Turbo** | **Paralinguistic only** — `[laugh]`, `[cough]`, `[sigh]`, `[gasp]`, `[chuckle]` | Yes (5 sec sample) | **Exaggeration scalar** (0.0–2.0 dial) | No | Yes (community fork, ~472ms TTFL on 4090) | English (Original), 23 langs (Multilingual) | MIT |
| **Chatterbox Original** | None | Yes (5 sec sample) | Exaggeration scalar (0.0–2.0) | No | No (batch) | English | MIT |
| **Dia2** (Nari Labs) | **Non-verbal tags** — `(laughs)`, `(sighs)`, `(clears throat)`, `(gasps)`, `(coughs)`, `(singing)` | Yes (audio conditioning) | Via audio conditioning | 2 speakers `[S1]`/`[S2]` | No | English only | Apache 2.0 |
| **Kokoro** | **None** | No | None (neutral tone) | No | No | English only | Apache 2.0 |
| **VibeVoice 7B** | **None** | Community voice cloning (June 2026, streaming model only) | From text context only | Up to 4 speakers, 90 min | Realtime 0.5B variant | English, Mandarin + 9 experimental | MIT |
| **IndexTTS-2** | **Segment-level emotion** via QwenEmotion, style tags, or reference audio | Yes (zero-shot) | **Timbre/emotion disentangled** — mix one speaker's voice with another's emotion | No | No | English, Mandarin | Non-commercial (contact for commercial) |
| **CosyVoice 2** | Limited | Yes | Limited | No | Yes (low-latency) | Multi | Apache 2.0 |

### Audiobook Narration Quality Assessment

| Model | Voice Naturalness | Expressive Range | Long-Form Consistency | Performance Direction | Overall Narration Grade |
|-------|-------------------|------------------|----------------------|----------------------|------------------------|
| **Fish Audio S2-Pro** | Excellent | Excellent (open-domain tags) | Good (SGLang prefix caching for voice consistency) | **Best-in-class local** | **A** |
| **Chatterbox Turbo** | Very Good | Good (emotion dial + paralinguistics) | Good (seed + ref audio) | Limited (no stage directions) | **B+** |
| **Dia2** | Very Good (dialogue) | Good (non-verbals) | Moderate (seed-dependent) | Limited | **B** (dialogue), **C+** (narration) |
| **IndexTTS-2** | Excellent | Excellent (emotion disentanglement) | Good | Good (segment-level) | **A-** (but non-commercial license) |
| **Kokoro** | Good | Poor (flat/neutral) | Good | None | **C** (narration), **B+** (informational) |
| **VibeVoice 7B** | Good | Moderate | Moderate (odd artifacts) | None | **B-** |

---

## Section 3: Subscription-Only Services

Monthly commitment required to access quality tiers.

| Provider | Price | Inline Tags | Voice Cloning | Streaming | Multi-Speaker | Languages | Narration Quality |
|----------|-------|-------------|---------------|-----------|---------------|-----------|-------------------|
| **ElevenLabs v3** | $5–$330/mo | **Open-domain audio tags** — `[sarcastically]`, `[pirate voice]`, `[French accent]`, `[whispers]`, `[gunshot]`, `[explosion]` + sound FX | Yes (IVC + PVC, PVC not optimized for v3 yet) | HTTP only (full text up front, chunks back). **No WebSocket streaming for v3.** Flash v2.5 for real-time (75ms) but lacks tags. | Yes (Text to Dialogue mode) | 70+ | **Reference standard** |
| **Fish Audio** | $15–$100/mo | Same as self-hosted S2-Pro (open-domain inline) | Yes | Yes (sub-150ms) | Up to 8 | 80+ | Excellent |
| **Hume Octave 2** | $20–$900/mo | **Implicit from text context** — no tags needed, model infers emotion from prose. Also accepts natural-language direction ("sound cautiously optimistic") | Yes (15 sec sample) | Yes (<200ms) | Yes | 11 | Excellent emotional inference |

### ElevenLabs v3 — Key Caveats for Audiobook Use

- v3 is **batch-only** for expressive output — cannot do real-time streaming with audio tags
- **Non-deterministic** — may need multiple generations and cherry-pick best take
- **5,000 character limit per call** — must chunk chapters
- Professional Voice Clones not yet optimized for v3
- 80% off promo running through end of June 2026

### Hume AI — Caution

Previous model (Octave 1) had severe reliability issues including: inability to pronounce basic possessives ("Steven's"), and cross-contamination artifacts where fragments of unrelated speech appeared in output. Octave 2 claims improved pronunciation and reliability, but trust must be re-earned through testing.

---

## Section 4: Architecture Fit for DreamTide / Narrate Engine

### Recommended Two-Stage Pipeline

```
Raw Manuscript
    ↓
[FacetCognition Director Pass]
  LLM reads prose, identifies emotional beats,
  dialogue attribution, pacing needs.
  Outputs tagged script with inline direction.
    ↓
[Tagged Script]
  "I thought I was ready. [voice breaking] I wasn't.
   [long pause] [whispered] Maybe I never will be."
    ↓
[TTS Provider — swappable backend]
  ITtsProvider interface:
    - FishAudioS2Provider (self-hosted or API)
    - ChatterboxProvider (local, simpler tags)
    - ElevenLabsV3Provider (commercial, best quality)
    - FalAiProvider (Orpheus/Dia for quick tests)
    ↓
[Narrated Audio]
```

### Provider Recommendations by Use Case

| Use Case | Primary | Fallback | Rationale |
|----------|---------|----------|-----------|
| **DreamTide audiobook production** (batch, quality matters) | Fish S2-Pro self-hosted (INT8, 4080) | ElevenLabs v3 API (for A/B comparison) | Best quality-to-cost, full tag control, no ongoing fees |
| **StoryGame real-time narration** (streaming, latency matters) | Chatterbox Turbo (local, ~472ms TTFL) | Fish Audio API (streaming, sub-150ms) | Local = free + low latency; Fish API for premium quality |
| **Quick prototyping / evaluation** | fal.ai Orpheus or Dia | Fish Audio $15/mo Plus | Already integrated; minimal friction |
| **Multi-language narration** | Fish S2-Pro (80+ langs) | Chatterbox Multilingual (23 langs) | Broadest coverage with expressive control |

### Quick-Start: Fish S2-Pro on RTX 4080 (WSL2)

```bash
# Inside WSL2 Ubuntu
nvidia-smi  # verify 4080 is visible

sudo apt install portaudio19-dev libsox-dev ffmpeg
git clone https://github.com/fishaudio/fish-speech.git
cd fish-speech
conda create -n fish-speech python=3.12 && conda activate fish-speech
pip install -e .[cu129]

# INT8 quantized model (~5.1 GB, fits 16GB VRAM easily)
huggingface-cli download Imagilux/fishaudio-s2-pro \
  --local-dir checkpoints/fish-speech-s2-pro

# Launch with torch.compile for ~10x speedup
python app.py --compile
# → Gradio UI at http://localhost:7860
```

### Key References

- [Fish Audio S2 Docs](https://speech.fish.audio/) — installation, inline tag guide, API reference
- [Fish Audio Inline Tag Guide](https://fish.audio/blog/fish-audio-s2-fine-grained-ai-voice-control-at-the-word-level/) — word-level tag placement rules and tips
- [Fish S2-Pro Weights (bf16)](https://huggingface.co/fishaudio/s2-pro) — full model
- [Fish S2-Pro INT8 (AMD/VRAM-constrained)](https://huggingface.co/Imagilux/fishaudio-s2-pro) — quantized, ~5.1 GB
- [Chatterbox GitHub](https://github.com/resemble-ai/chatterbox) — MIT licensed, all 3 variants
- [Chatterbox Streaming Fork](https://github.com/davidbrowne17/chatterbox-streaming) — community realtime streaming
- [Dia2 GitHub](https://github.com/nari-labs/dia) — Apache 2.0, dialogue-focused
- [ElevenLabs v3 Audio Tags Guide](https://elevenlabs.io/blog/v3-audiotags) — tag syntax and best practices
- [ElevenLabs v3 Best Practices](https://elevenlabs.io/docs/overview/capabilities/text-to-speech/best-practices) — prompting guide
- [MarkTechPost TTS Benchmark Comparison (May 2026)](https://www.marktechpost.com/2026/05/30/best-text-to-speech-tts-models-in-2026-a-benchmark-based-comparison/)
- [fal.ai TTS Models](https://fal.ai/models) — Orpheus, Dia, Gemini TTS, xAI TTS, F5-TTS