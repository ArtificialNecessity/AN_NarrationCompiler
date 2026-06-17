namespace Mirica.Desktop.Providers.TextToSpeech;

/// <summary>
/// A batch of word timestamps from a TTS provider.
/// Provider-neutral representation (not tied to Cartesia's SoA wire format).
/// </summary>
public record struct WordTimestampBatch(
    string[] Words,
    float[] StartTimes,  // seconds relative to audio stream start
    float[] EndTimes     // seconds relative to audio stream start
);

// ─── Provider Interfaces ─────────────────────────────────────────────────────

/// <summary>
/// Generic streaming TTS provider interface.
/// Implementations connect to a TTS service, send text incrementally,
/// and yield PCM audio chunks for playback.
/// </summary>
public interface ITTSProvider
{
    string ProviderId { get; }
    string ProviderName { get; }

    /// <summary>
    /// Initialize the provider with credentials.
    /// </summary>
    Task InitializeAsync(string apiKey, string voiceId);

    /// <summary>
    /// Whether the provider is initialized and ready to synthesize.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Begin a new streaming synthesis turn.
    /// Opens a WebSocket connection (or equivalent) for this turn.
    /// </summary>
    Task<ITTSSession> StartSessionAsync();

    /// <summary>
    /// Clean up resources.
    /// </summary>
    Task DisposeAsync();
}

/// <summary>
/// Represents a single turn's TTS synthesis session.
/// Text is sent incrementally; audio chunks arrive via callback.
/// </summary>
public interface ITTSSession : IAsyncDisposable
{
    /// <summary>
    /// Whether the session's underlying connection is still alive.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Send a text fragment to the TTS engine.
    /// Called repeatedly as narrative tokens arrive.
    /// </summary>
    Task SendTextAsync(string text);

    /// <summary>
    /// Signal that no more text will be sent for this turn.
    /// The provider should flush any remaining audio.
    /// </summary>
    Task FlushAndCloseAsync();

    /// <summary>
    /// Fired when a PCM audio chunk is received from the TTS service.
    /// byte[] is raw PCM S16LE at the configured sample rate.
    /// </summary>
    event Action<byte[]>? OnAudioChunkReceived;

    /// <summary>
    /// Fired when synthesis is fully complete (all audio delivered).
    /// </summary>
    event Action? OnComplete;

    /// <summary>
    /// Fired on error.
    /// </summary>
    event Action<Exception>? OnError;

    /// <summary>
    /// Fired when word timestamp data is received from the TTS service.
    /// </summary>
    event Action<WordTimestampBatch>? OnWordTimestamps;
}