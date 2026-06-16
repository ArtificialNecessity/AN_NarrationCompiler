using AstroCryptKit;
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

        var pcmData = await provider.RenderTextToPcmAsync(chapter.ProseContent);
        if (pcmData == null || pcmData.Length == 0)
        {
            Console.Error.WriteLine("[ERROR] No audio data received from IndexTTS2.");
            return 1;
        }

        // Write WAV file
        var wavPath = Path.Combine(outputDir, wavFileName);
        WavWriter.WritePcmToWav(wavPath, pcmData, provider.OutputSampleRate);

        var durationSec = (double)pcmData.Length / (provider.OutputSampleRate * 2); // 16-bit mono = 2 bytes/sample
        Console.WriteLine($"[DONE] {wavPath}");
        Console.WriteLine($"       Duration: {TimeSpan.FromSeconds(durationSec):mm':'ss} | Size: {pcmData.Length / 1024:N0} KB");

        manifest.RecordRender(wavFileName, contentHash);
        manifest.Save();

        await provider.DisposeAsync();
        return 0;
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
}