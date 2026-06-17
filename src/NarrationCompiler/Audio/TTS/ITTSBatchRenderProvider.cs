namespace Mirica.Desktop.Providers.TextToSpeech;

/// <summary>
/// Batch/REST-based TTS provider interface.
/// Unlike the streaming ITTSProvider (designed for WebSocket incremental delivery),
/// this interface handles providers that accept full text and return a complete audio file.
/// Examples: FAL.ai, Replicate, or any REST-based TTS API.
/// </summary>
public interface ITTSBatchRenderProvider
{
    string ProviderId { get; }
    string ProviderName { get; }

    /// <summary>
    /// Initialize the provider with credentials and voice reference.
    /// For batch providers, voiceReference may be a URL or local file path to a reference audio.
    /// </summary>
    Task InitializeAsync(string apiKey, string voiceReference);

    /// <summary>
    /// Whether the provider is initialized and ready to synthesize.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Render the given text to PCM audio bytes (S16LE, mono).
    /// The provider handles the full round-trip: send text, wait for result, download, decode to PCM.
    /// </summary>
    /// <param name="text">Full text to synthesize.</param>
    /// <param name="options">Optional rendering parameters.</param>
    /// <returns>Raw PCM bytes (S16LE mono at the provider's sample rate), or null on failure.</returns>
    Task<byte[]?> RenderTextToPcmAsync(string text, BatchRenderOptions? options = null);

    /// <summary>
    /// The sample rate of the PCM audio returned by RenderTextToPcmAsync.
    /// </summary>
    int OutputSampleRate { get; }

    /// <summary>
    /// Clean up resources.
    /// </summary>
    Task DisposeAsync();
}

/// <summary>
/// Options for batch TTS rendering.
/// </summary>
public class BatchRenderOptions
{
    /// <summary>
    /// Optional emotion prompt — a text description or sentence that conveys the desired emotional tone.
    /// The provider may use this to influence the speech style.
    /// </summary>
    public string? EmotionPrompt { get; init; }

    /// <summary>
    /// Whether to automatically derive emotional style from the main text prompt.
    /// If true and EmotionPrompt is null, the synthesis text itself is used for emotion extraction.
    /// </summary>
    public bool UsePromptForEmotion { get; init; } = false;

    /// <summary>
    /// Strength of emotional style transfer (0.0 to 1.0).
    /// Higher values produce stronger emotional influence.
    /// </summary>
    public float EmotionStrength { get; init; } = 1.0f;

    /// <summary>
    /// Optional URL to an emotional reference audio file.
    /// Used by providers that support extracting emotion from a separate audio sample.
    /// </summary>
    public string? EmotionalAudioUrl { get; init; }
}