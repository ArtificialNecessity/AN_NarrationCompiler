using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
// DebugSplatter replaced with Console.WriteLine for standalone use

namespace Mirica.Desktop.Providers.TextToSpeech.CartesiaProvider;

/// <summary>
/// Cartesia TTS provider using WebSocket streaming API.
/// Each turn opens a new WebSocket connection, sends text incrementally,
/// and receives PCM S16LE audio chunks.
/// </summary>
public class CartesiaTTSProvider : ITTSProvider
{
    private const string DebugTag = "mirica/audio/cartesia";

    private string _apiKey = string.Empty;
    private string _voiceId = string.Empty;
    private int _sampleRate = 44100;

    public string ProviderId => "cartesia";
    public string ProviderName => "Cartesia TTS";
    public bool IsReady { get; private set; }

    public Task InitializeAsync(string apiKey, string voiceId)
    {
        _apiKey = apiKey;
        _voiceId = voiceId;
        IsReady = !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(voiceId);
        DebugSplatter.Info(DebugTag, $"Initialized (ready={IsReady}, sampleRate={_sampleRate})");
        return Task.CompletedTask;
    }

    public async Task<ITTSSession> StartSessionAsync()
    {
        if (!IsReady)
            throw new InvalidOperationException("CartesiaTTSProvider not initialized");

        var session = new CartesiaTTSSession(_apiKey, _voiceId, _sampleRate);
        await session.ConnectAsync();
        return session;
    }

    public Task DisposeAsync()
    {
        IsReady = false;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A single turn's TTS session over a Cartesia WebSocket.
/// Sends text fragments with continue=true, then flushes with continue=false.
/// Audio chunks arrive as base64-encoded PCM S16LE.
/// </summary>
public class CartesiaTTSSession : ITTSSession
{
    private const string DebugTag = "mirica/audio/cartesia-session";

    private readonly string _apiKey;
    private readonly string _voiceId;
    private readonly int _sampleRate;
    private readonly string _contextId;
    private ClientWebSocket? _ws;
    private CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private bool _disposed;
    private bool _flushed;

    public event Action<byte[]>? OnAudioChunkReceived;
    public event Action? OnComplete;
    public event Action<Exception>? OnError;
    public event Action<WordTimestampBatch>? OnWordTimestamps;

    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open && !_disposed;

    public CartesiaTTSSession(string apiKey, string voiceId, int sampleRate)
    {
        _apiKey = apiKey;
        _voiceId = voiceId;
        _sampleRate = sampleRate;
        _contextId = $"ctx_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];
    }

    public async Task ConnectAsync()
    {
        _ws = new ClientWebSocket();
        var uri = new Uri($"wss://api.cartesia.ai/tts/websocket?api_key={_apiKey}&cartesia_version=2024-06-10");

        try
        {
            await _ws.ConnectAsync(uri, _cts.Token);
            DebugSplatter.Info(DebugTag, $"WebSocket connected (context={_contextId})");

            // Start background receive loop
            _receiveTask = Task.Run(ReceiveLoopAsync);
        }
        catch (Exception ex)
        {
            var innerMsg = ex.InnerException != null ? $" -> {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
            DebugSplatter.Info(DebugTag, $"WebSocket connection failed: {ex.GetType().Name}: {ex.Message}{innerMsg}");
            OnError?.Invoke(ex);
            throw;
        }
    }

    public async Task SendTextAsync(string text)
    {
        if (_ws == null || _ws.State != WebSocketState.Open || _disposed)
            return;

        var payload = new
        {
            model_id = "sonic-3",
            transcript = text,
            voice = new { mode = "id", id = _voiceId },
            context_id = _contextId,
            @continue = true,
            add_timestamps = true,
            output_format = new
            {
                container = "raw",
                encoding = "pcm_s16le",
                sample_rate = _sampleRate
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
    }

    public async Task FlushAndCloseAsync()
    {
        if (_flushed || _ws == null || _ws.State != WebSocketState.Open || _disposed)
            return;

        _flushed = true;

        var payload = new
        {
            model_id = "sonic-3",
            transcript = "",
            voice = new { mode = "id", id = _voiceId },
            context_id = _contextId,
            @continue = false,
            add_timestamps = true,
            output_format = new
            {
                container = "raw",
                encoding = "pcm_s16le",
                sample_rate = _sampleRate
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
            DebugSplatter.Debug(DebugTag, $"Flush sent (context={_contextId})");

            if (_receiveTask != null)
                await Task.WhenAny(_receiveTask, Task.Delay(TimeSpan.FromSeconds(30)));
        }
        catch (Exception ex)
        {
            DebugSplatter.Info(DebugTag, $"Error during flush: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (_ws != null && _ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    DebugSplatter.Debug(DebugTag, "WebSocket closed by server");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessMessage(json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            DebugSplatter.Debug(DebugTag, "WebSocket closed prematurely");
        }
        catch (Exception ex)
        {
            DebugSplatter.Info(DebugTag, $"Receive loop error: {ex.Message}");
            OnError?.Invoke(ex);
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "chunk":
                    if (root.TryGetProperty("data", out var dataElement))
                    {
                        var base64Data = dataElement.GetString();
                        if (!string.IsNullOrEmpty(base64Data))
                        {
                            var pcmBytes = Convert.FromBase64String(base64Data);
                            OnAudioChunkReceived?.Invoke(pcmBytes);
                        }
                    }
                    break;

                case "timestamps":
                    if (root.TryGetProperty("word_timestamps", out var tsElement))
                    {
                        var words = tsElement.GetProperty("words");
                        var starts = tsElement.GetProperty("start");
                        var ends = tsElement.GetProperty("end");

                        int count = words.GetArrayLength();
                        var wordArr = new string[count];
                        var startArr = new float[count];
                        var endArr = new float[count];

                        for (int i = 0; i < count; i++)
                        {
                            wordArr[i] = words[i].GetString() ?? "";
                            startArr[i] = (float)starts[i].GetDouble();
                            endArr[i] = (float)ends[i].GetDouble();
                        }

                        var batch = new WordTimestampBatch(wordArr, startArr, endArr);
                        DebugSplatter.Debug(DebugTag, $"Timestamps: {count} words");
                        OnWordTimestamps?.Invoke(batch);
                    }
                    break;

                case "done":
                    DebugSplatter.Debug(DebugTag, $"Synthesis complete (context={_contextId})");
                    OnComplete?.Invoke();
                    return;

                case "error":
                    var errorMsg = root.TryGetProperty("message", out var msgEl)
                        ? msgEl.GetString() ?? "Unknown error"
                        : "Unknown Cartesia error";
                    DebugSplatter.Info(DebugTag, $"Cartesia error: {errorMsg}");
                    OnError?.Invoke(new Exception($"Cartesia TTS error: {errorMsg}"));
                    return;
            }
        }
        catch (JsonException ex)
        {
            DebugSplatter.Debug(DebugTag, $"Failed to parse message: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();

        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
                catch { /* Best-effort close */ }
            }
            _ws.Dispose();
            _ws = null;
        }

        _cts.Dispose();
    }
}