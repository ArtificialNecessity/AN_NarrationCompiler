using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mirica.Desktop.Providers.TextToSpeech.FishS2Provider;

/// <summary>
/// Fish Audio S2 TTS provider talking to a self-hosted fish-speech server.
/// Posts full text to POST {baseUrl}/v1/tts (application/json, streaming=false)
/// and receives a complete WAV, which is decoded to raw PCM S16LE.
///
/// Emotion/prosody is controlled by inline [tags] embedded directly in the text
/// (e.g. "[excited] Hello! [whisper] keep it down"). The IndexTTS2-style emotion
/// fields on BatchRenderOptions do not map to S2 and are ignored by this provider.
/// </summary>
public class FishS2LocalProvider : ITTSBatchRenderProvider
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;

    private string _bearerToken = string.Empty;   // optional; only if the server sets API_KEY
    private string _voiceReference = string.Empty; // file path (inline clone) | bare id (reference_id) | empty (default voice)

    public string ProviderId => "fish-s2-local";
    public string ProviderName => "Fish Audio S2 (local server)";
    public bool IsReady { get; private set; }
    public int OutputSampleRate { get; private set; } = 44100; // overwritten from each WAV header

    /// <param name="serverBaseUrl">Base URL of the fish-speech API server (the --profile server container).</param>
    public FishS2LocalProvider(string serverBaseUrl = "http://localhost:8080")
    {
        _baseUrl = serverBaseUrl.TrimEnd('/');
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <param name="apiKey">Optional bearer token. Empty for a local server with no auth.</param>
    /// <param name="voiceReference">Reference audio file path (voice cloning), a pre-registered reference id, or empty for the default voice.</param>
    public Task InitializeAsync(string apiKey, string voiceReference)
    {
        _bearerToken = apiKey ?? string.Empty;
        _voiceReference = voiceReference ?? string.Empty;

        // A reachable server is the only hard requirement; no API key needed for local.
        IsReady = true;

        if (!string.IsNullOrEmpty(_voiceReference) && !File.Exists(_voiceReference) && LooksLikePath(_voiceReference))
        {
            Console.Error.WriteLine($"[{ProviderId}] WARN: voice reference path not found, will fall back to default voice: {_voiceReference}");
        }

        Console.WriteLine($"[{ProviderId}] Initialized (server={_baseUrl}, ready={IsReady})");
        return Task.CompletedTask;
    }

    public async Task<byte[]?> RenderTextToPcmAsync(string text, BatchRenderOptions? options = null)
    {
        if (!IsReady)
            throw new InvalidOperationException("FishS2LocalProvider not initialized");

        var payload = await BuildRequestPayloadAsync(text);
        var json = JsonSerializer.Serialize(payload);

        Console.WriteLine($"[{ProviderId}] Sending request ({text.Length:N0} chars) to {_baseUrl}/v1/tts ...");

        byte[]? audioBytes;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/tts");
            if (!string.IsNullOrEmpty(_bearerToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            // Ask for audio back rather than a JSON envelope.
            request.Headers.Accept.ParseAdd("audio/wav");

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                Console.Error.WriteLine($"[{ProviderId}] API error {(int)response.StatusCode}: {Truncate(err, 500)}");
                return null;
            }

            audioBytes = await response.Content.ReadAsByteArrayAsync();
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine($"[{ProviderId}] Request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[{ProviderId}] HTTP error: {ex.Message} (is the fish-speech server running at {_baseUrl}?)");
            return null;
        }

        if (audioBytes.Length == 0)
        {
            Console.Error.WriteLine($"[{ProviderId}] Empty response body");
            return null;
        }

        var pcm = DecodeWavToPcm(audioBytes);
        if (pcm == null)
        {
            Console.Error.WriteLine($"[{ProviderId}] Response was not a parseable WAV ({audioBytes.Length} bytes)");
            return null;
        }

        Console.WriteLine($"[{ProviderId}] Generated {pcm.Length / 1024:N0} KB PCM @ {OutputSampleRate} Hz");
        return pcm;
    }

    public Task DisposeAsync()
    {
        IsReady = false;
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    // ─── Request building ────────────────────────────────────────────────────────

    private async Task<Dictionary<string, object>> BuildRequestPayloadAsync(string text)
    {
        // Field names + ranges mirror fish_speech ServeTTSRequest.
        var payload = new Dictionary<string, object>
        {
            ["text"] = text,
            ["format"] = "wav",
            ["streaming"] = false,
            ["normalize"] = true,
            ["chunk_length"] = 200,           // ge=100, le=1000
            ["max_new_tokens"] = 1024,
            ["top_p"] = 0.8,                  // 0.1..1.0
            ["repetition_penalty"] = 1.1,     // 0.9..2.0
            ["temperature"] = 0.8,            // 0.1..1.0
            ["use_memory_cache"] = "on",     // reuse the resident reference across chunks
        };

        if (string.IsNullOrEmpty(_voiceReference))
        {
            // default voice
        }
        else if (File.Exists(_voiceReference))
        {
            var audioBytes = await File.ReadAllBytesAsync(_voiceReference);
            payload["references"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["audio"] = Convert.ToBase64String(audioBytes),
                    ["text"] = ReadReferenceTranscript(_voiceReference),
                }
            };
        }
        else
        {
            // Not a file → treat as a pre-registered reference id (added via /v1/references/add).
            payload["reference_id"] = _voiceReference;
        }

        return payload;
    }

    /// <summary>
    /// Fish clones best when given the reference audio's transcript. By convention we look
    /// for a sibling text file ("voice.wav" → "voice.txt"); absent that, send empty text.
    /// </summary>
    private static string ReadReferenceTranscript(string audioPath)
    {
        var txtPath = Path.ChangeExtension(audioPath, ".txt");
        if (File.Exists(txtPath))
        {
            try { return File.ReadAllText(txtPath).Trim(); }
            catch { /* fall through to empty */ }
        }
        return string.Empty;
    }

    // ─── WAV → PCM ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Decode a PCM WAV into its raw S16LE sample bytes, reading the real sample rate
    /// from the header. Locates the 'data' chunk rather than assuming a 44-byte header.
    /// </summary>
    private byte[]? DecodeWavToPcm(byte[] wav)
    {
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
            return null;

        OutputSampleRate = BitConverter.ToInt32(wav, 24);
        int channels = BitConverter.ToInt16(wav, 22);
        if (channels != 1)
            Console.Error.WriteLine($"[{ProviderId}] WARN: WAV has {channels} channels; downstream WAV writer assumes mono.");

        int dataOffset = -1, dataSize = 0;
        for (int i = 12; i < wav.Length - 8; i++)
        {
            if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
            {
                dataSize = BitConverter.ToInt32(wav, i + 4);
                dataOffset = i + 8;
                break;
            }
        }

        if (dataOffset < 0 || dataOffset + dataSize > wav.Length)
        {
            dataOffset = 44;
            dataSize = wav.Length - 44;
        }

        var pcm = new byte[dataSize];
        Buffer.BlockCopy(wav, dataOffset, pcm, 0, dataSize);
        return pcm;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static bool LooksLikePath(string s) =>
        s.Contains('/') || s.Contains('\\') || s.Contains('.');

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
