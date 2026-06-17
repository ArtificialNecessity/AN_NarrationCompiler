using AstroCryptKit;
using System.Text;
using Mirica.Desktop.Providers.TextToSpeech;
using Mirica.Desktop.Providers.TextToSpeech.CartesiaProvider;
using Mirica.Desktop.Providers.TextToSpeech.IndexTTS2Provider;
using NarrationCompiler.Utils;

namespace NarrationCompiler;

/// <summary>
/// Implements the "render-one" command: renders a single chapter file to a WAV.
/// Supports both streaming (Cartesia) and batch (IndexTTS2 via FAL) providers.
/// </summary>
public static class RenderOneCommand
{
    private const int SampleRate = 44100;

    public static async Task<int> ExecuteAsync(AstroCryptKeystore keystore, string chapterPath, string? voiceIdOverride, string? outputDir = null, string? providerName = null)
    {
        // Determine which provider to use
        var useBatchProvider = string.Equals(providerName, "indextts2-fal", StringComparison.OrdinalIgnoreCase);

        // 1. Parse the chapter
        Console.WriteLine($"Parsing: {chapterPath}");
        var chapter = ChapterParser.Parse(chapterPath);
        if (chapter == null)
            return 1;

        // 2. Resolve credentials and voice reference
        string apiKey;
        string voiceId;

        if (useBatchProvider)
        {
            apiKey = keystore.RevealDangerouslySecretKeyValue("fal_api_key");
            voiceId = voiceIdOverride ?? keystore.RevealDangerouslySecretKeyValue("indextts2_voice_url");
        }
        else
        {
            apiKey = keystore.RevealDangerouslySecretKeyValue("cartesia_api_key");
            voiceId = voiceIdOverride ?? keystore.RevealDangerouslySecretKeyValue("cartesia_voice_id");
        }

        var voiceDisplay = voiceId.Length > 40
            ? $"{voiceId[..20]}...{voiceId[^15..]}"
            : voiceId.Length > 8 ? $"{voiceId[..8]}..." : voiceId;
        Console.WriteLine($"Provider: {providerName ?? "cartesia"}");
        Console.WriteLine($"Voice: {voiceDisplay}");

        // 3. Check manifest — skip if already rendered with same content+voice
        var resolvedOutputDir = outputDir ?? Path.GetDirectoryName(Path.GetFullPath(chapterPath)) ?? ".";
        Directory.CreateDirectory(resolvedOutputDir);
        var baseName = Path.GetFileNameWithoutExtension(chapterPath);
        var wavFileName = $"{baseName}.wav";
        var contentHash = RenderManifest.ComputeHash(chapter.ProseContent, voiceId);
        var manifest = RenderManifest.Load(resolvedOutputDir);

        if (!manifest.NeedsRender(wavFileName, contentHash))
        {
            Console.WriteLine($"  [CACHED] {wavFileName} \u2014 content unchanged, skipping.");
            return 0;
        }

        // 4. Render using the appropriate provider
        if (useBatchProvider)
        {
            return await RenderWithBatchProvider(apiKey, voiceId, chapter, resolvedOutputDir, wavFileName, contentHash, manifest);
        }
        else
        {
            return await RenderWithStreamingProvider(apiKey, voiceId, chapter, resolvedOutputDir, wavFileName, contentHash, manifest);
        }
    }

    // ─── Batch Provider (IndexTTS2 via FAL) ─────────────────────────────────────

    private static async Task<int> RenderWithBatchProvider(
        string apiKey, string voiceId, ParsedChapter chapter,
        string outputDir, string wavFileName, string contentHash, RenderManifest manifest)
    {
        var provider = new IndexTTS2viaFAL();
        await provider.InitializeAsync(apiKey, voiceId);

        if (!provider.IsReady)
        {
            Console.Error.WriteLine("[ERROR] IndexTTS2 provider failed to initialize.");
            return 1;
        }

        Console.WriteLine($"Rendering: \"{chapter.ChapterTitle}\" ({chapter.ProseContent.Length:N0} chars)...");

        // Chunk the text on paragraph/section boundaries to avoid API timeouts
        var chunks = ChunkTextForBatchRender(chapter.ProseContent);
        Console.WriteLine($"  Split into {chunks.Count} chunk(s) for batch rendering.");

        int result = await RenderChunksToNumberedWavs(provider, chunks, outputDir, wavFileName, contentHash, manifest);

        await provider.DisposeAsync();
        return result;
    }

    // ─── Streaming Provider (Cartesia) ────────────────────────────────────────

    private static async Task<int> RenderWithStreamingProvider(
        string apiKey, string voiceId, ParsedChapter chapter,
        string outputDir, string wavFileName, string contentHash, RenderManifest manifest)
    {
        var provider = new CartesiaTTSProvider();
        await provider.InitializeAsync(apiKey, voiceId);

        if (!provider.IsReady)
        {
            Console.Error.WriteLine("[ERROR] TTS provider failed to initialize.");
            return 1;
        }

        Console.WriteLine($"Rendering (streaming): \"{chapter.ChapterTitle}\" ({chapter.ProseContent.Length:N0} chars)...");

        var pcmData = await RenderChapterToPcm(provider, chapter.ProseContent);
        if (pcmData == null || pcmData.Length == 0)
        {
            Console.Error.WriteLine("[ERROR] No audio data received from TTS.");
            return 1;
        }

        // Write WAV file
        var wavPath = Path.Combine(outputDir, wavFileName);
        WavWriter.WritePcmToWav(wavPath, pcmData, SampleRate);

        var durationSec = (double)pcmData.Length / (SampleRate * 2); // 16-bit mono = 2 bytes/sample
        Console.WriteLine($"[DONE] {wavPath}");
        Console.WriteLine($"       Duration: {TimeSpan.FromSeconds(durationSec):mm':'ss} | Size: {pcmData.Length / 1024:N0} KB");

        manifest.RecordRender(wavFileName, contentHash);
        manifest.Save();

        await provider.DisposeAsync();
        return 0;
    }

    private static async Task<byte[]?> RenderChapterToPcm(CartesiaTTSProvider provider, string proseText)
    {
        var session = await provider.StartSessionAsync();
        var pcmChunks = new List<byte[]>();
        var completionTcs = new TaskCompletionSource<bool>();
        int totalWordCount = proseText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        int wordsRendered = 0;

        session.OnAudioChunkReceived += chunk =>
        {
            pcmChunks.Add(chunk);
        };

        session.OnComplete += () =>
        {
            completionTcs.TrySetResult(true);
            Console.WriteLine(); // newline after progress
        };

        session.OnWordTimestamps += batch =>
        {
            wordsRendered += batch.Words.Length;
            int pct = totalWordCount > 0 ? Math.Min(100, (int)(wordsRendered * 100L / totalWordCount)) : 0;
            Console.Write($"\r  Rendering: {pct,3}% ({wordsRendered:N0}/{totalWordCount:N0} words)");
        };

        session.OnError += ex =>
        {
            Console.WriteLine(); // newline after progress
            Console.Error.WriteLine($"\n[TTS ERROR] {ex.Message}");
            completionTcs.TrySetResult(false);
        };

        // Send the full prose text in chunks to avoid WebSocket message size limits
        // Cartesia handles sentence boundaries internally
        const int chunkSize = 1000;
        for (int i = 0; i < proseText.Length; i += chunkSize)
        {
            var textChunk = proseText.Substring(i, Math.Min(chunkSize, proseText.Length - i));
            await session.SendTextAsync(textChunk);
        }

        // Signal end of text and wait for all audio
        await session.FlushAndCloseAsync();

        // Wait for completion with timeout
        var completed = await Task.WhenAny(completionTcs.Task, Task.Delay(TimeSpan.FromMinutes(10)));
        if (completed != completionTcs.Task)
        {
            Console.Error.WriteLine("[ERROR] TTS render timed out after 10 minutes.");
            await session.DisposeAsync();
            return null;
        }

        await session.DisposeAsync();

        if (!completionTcs.Task.Result)
            return null;

        // Concatenate all PCM chunks
        var totalLength = pcmChunks.Sum(c => c.Length);
        var result = new byte[totalLength];
        int offset = 0;
        foreach (var chunk in pcmChunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    // ─── Chunking for Batch Provider ─────────────────────────────────────────────

    /// <summary>
    /// Maximum characters per chunk sent to the batch TTS provider.
    /// IndexTTS2 via FAL times out on very long texts; this keeps each request manageable.
    /// </summary>
    private const int MaxChunkChars = 3000;

    /// <summary>
    /// Split prose text into chunks suitable for batch TTS rendering.
    /// Splits on section breaks (---) first, then on paragraph boundaries (\n\n),
    /// enforcing a maximum character limit per chunk.
    /// </summary>
    private static List<string> ChunkTextForBatchRender(string proseText)
    {
        // First, split on section breaks (--- on its own line)
        var sections = SplitOnSectionBreaks(proseText);

        // Then, for each section, split further on paragraph boundaries if too large
        var chunks = new List<string>();
        foreach (var section in sections)
        {
            if (section.Length <= MaxChunkChars)
            {
                chunks.Add(section);
            }
            else
            {
                // Split on paragraph boundaries (\n\n)
                chunks.AddRange(SplitOnParagraphs(section, MaxChunkChars));
            }
        }

        return chunks;
    }

    /// <summary>
    /// Split text on "---" lines (horizontal rules / section breaks).
    /// </summary>
    private static List<string> SplitOnSectionBreaks(string text)
    {
        var sections = new List<string>();
        var lines = text.Split('\n');
        var currentSection = new List<string>();

        foreach (var line in lines)
        {
            if (line.Trim() == "---")
            {
                if (currentSection.Count > 0)
                {
                    var sectionText = string.Join("\n", currentSection).Trim();
                    if (sectionText.Length > 0)
                        sections.Add(sectionText);
                    currentSection.Clear();
                }
            }
            else
            {
                currentSection.Add(line);
            }
        }

        // Don't forget the last section
        if (currentSection.Count > 0)
        {
            var sectionText = string.Join("\n", currentSection).Trim();
            if (sectionText.Length > 0)
                sections.Add(sectionText);
        }

        return sections;
    }

    /// <summary>
    /// Split a section into chunks on paragraph boundaries (\n\n), respecting maxChars.
    /// Paragraphs are never split mid-paragraph — if a single paragraph exceeds maxChars,
    /// it becomes its own chunk.
    /// </summary>
    private static List<string> SplitOnParagraphs(string section, int maxChars)
    {
        var paragraphs = section.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var para in paragraphs)
        {
            var trimmedPara = para.Trim();
            if (trimmedPara.Length == 0) continue;

            // If adding this paragraph would exceed the limit, flush current chunk
            if (currentChunk.Length > 0 && currentChunk.Length + trimmedPara.Length + 2 > maxChars)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            if (currentChunk.Length > 0)
                currentChunk.Append("\n\n");
            currentChunk.Append(trimmedPara);
        }

        // Flush remaining
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    /// <summary>
    /// Render text chunks sequentially, writing each as a separate numbered WAV file.
    /// Files are named: baseName_001.wav, baseName_002.wav, etc.
    /// Each file is written immediately after rendering, so partial progress is preserved.
    /// </summary>
    private static async Task<int> RenderChunksToNumberedWavs(
        IndexTTS2viaFAL provider, List<string> chunks,
        string outputDir, string wavFileName, string contentHash, RenderManifest manifest)
    {
        var baseName = Path.GetFileNameWithoutExtension(wavFileName);
        int successCount = 0;
        double totalDuration = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            Console.WriteLine($"  [{i + 1}/{chunks.Count}] Rendering chunk ({chunk.Length:N0} chars)...");

            var pcm = await provider.RenderTextToPcmAsync(chunk);
            if (pcm == null || pcm.Length == 0)
            {
                Console.Error.WriteLine($"  [{i + 1}/{chunks.Count}] FAILED — no audio returned.");
                continue;
            }

            // Write this chunk immediately as a numbered WAV
            var chunkFileName = chunks.Count == 1
                ? wavFileName
                : $"{baseName}_{(i + 1):D3}.wav";
            var chunkPath = Path.Combine(outputDir, chunkFileName);
            WavWriter.WritePcmToWav(chunkPath, pcm, provider.OutputSampleRate);

            var durationSec = (double)pcm.Length / (provider.OutputSampleRate * 2);
            totalDuration += durationSec;
            successCount++;

            Console.WriteLine($"  [{i + 1}/{chunks.Count}] OK — {chunkFileName} ({durationSec:F1}s, {pcm.Length / 1024:N0} KB)");
        }

        if (successCount == 0)
        {
            Console.Error.WriteLine("[ERROR] No chunks rendered successfully.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"[DONE] {successCount}/{chunks.Count} chunks rendered ({totalDuration:F1}s total)");
        Console.WriteLine($"       Output: {outputDir}");

        manifest.RecordRender(wavFileName, contentHash);
        manifest.Save();

        return 0;
    }
}