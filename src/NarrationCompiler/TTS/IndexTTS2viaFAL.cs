using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirica.Desktop.Providers.TextToSpeech.IndexTTS2Provider;

/// <summary>
/// IndexTTS2 TTS provider via FAL.ai REST API.
/// Sends full text + voice reference audio URL, receives a generated audio file URL.
/// Supports basic emotion control via prompt-based emotion extraction.
/// </summary>
public class IndexTTS2viaFAL : ITTSBatchRenderProvider
{
    private const string DebugTag = "narration/tts/indextts2-fal";
    private const string FalEndpoint = "https://fal.run/fal-ai/index-tts-2/text-to-speech";

    private string _apiKey = string.Empty;
    private string _voiceReference = string.Empty; // URL or local file path to reference audio
    private readonly HttpClient _httpClient;

    public string ProviderId => "indextts2-fal";
    public string ProviderName => "IndexTTS2 via FAL.ai";
    public bool IsReady { get; private set; }
    public int OutputSampleRate { get; private set; } = 24000;
    private int _detectedChannels = 1;

    public IndexTTS2viaFAL()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // TTS generation can take a while
    }

    public Task InitializeAsync(string apiKey, string voiceReference)
    {
        _apiKey = apiKey;
        _voiceReference = voiceReference;
        IsReady = !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(voiceReference);

        // Validate voice reference is a URL or an existing file
        if (IsReady && !IsUrl(_voiceReference) && !File.Exists(_voiceReference))
        {
            Console.Error.WriteLine($"[WARN] Voice reference is neither a URL nor an existing file: {_voiceReference}");
            // Don't fail — the user may set it up later, or it may be a relative path resolved at render time
        }

        Console.WriteLine($"[{ProviderId}] Initialized (ready={IsReady})");
        return Task.CompletedTask;
    }

    public async Task<byte[]?> RenderTextToPcmAsync(string text, BatchRenderOptions? options = null)
    {
        if (!IsReady)
            throw new InvalidOperationException("IndexTTS2viaFAL not initialized");

        // Resolve voice reference to a URL
        var audioUrl = await ResolveVoiceReferenceAsync(_voiceReference);
        if (audioUrl == null)
        {
            Console.Error.WriteLine($"[{ProviderId}] Failed to resolve voice reference: {_voiceReference}");
            return null;
        }

        // Build request payload
        var requestBody = BuildRequestPayload(text, audioUrl, options);
        var jsonContent = JsonSerializer.Serialize(requestBody, FalJsonContext.Default.DictionaryStringObject);

        // Make the API call
        Console.WriteLine($"[{ProviderId}] Sending request ({text.Length:N0} chars)...");
        var response = await CallFalApiAsync(jsonContent);
        if (response == null)
            return null;

        // Parse response to get audio URL
        var outputAudioUrl = ParseResponseAudioUrl(response);
        if (outputAudioUrl == null)
        {
            Console.Error.WriteLine($"[{ProviderId}] Failed to parse audio URL from response");
            return null;
        }

        // Download the generated audio file
        Console.WriteLine($"[{ProviderId}] Downloading generated audio...");
        var audioBytes = await DownloadAudioAsync(outputAudioUrl);
        if (audioBytes == null || audioBytes.Length == 0)
        {
            Console.Error.WriteLine($"[{ProviderId}] Failed to download generated audio");
            return null;
        }

        // Decode the audio file (likely mp3/wav) to raw PCM
        var pcmBytes = DecodeAudioToPcm(audioBytes, outputAudioUrl);
        if (pcmBytes == null)
        {
            Console.Error.WriteLine($"[{ProviderId}] Failed to decode audio to PCM");
            return null;
        }

        Console.WriteLine($"[{ProviderId}] Generated {pcmBytes.Length / 1024:N0} KB PCM audio");
        return pcmBytes;
    }

    public Task DisposeAsync()
    {
        IsReady = false;
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    // ─── Private Helpers ────────────────────────────────────────────────────────

    private Dictionary<string, object> BuildRequestPayload(string text, string audioUrl, BatchRenderOptions? options)
    {
        var payload = new Dictionary<string, object>
        {
            ["audio_url"] = audioUrl,
            ["prompt"] = text
        };

        if (options != null)
        {
            // Basic emotion support via prompt-based emotion extraction
            if (options.UsePromptForEmotion)
            {
                payload["should_use_prompt_for_emotion"] = true;

                if (!string.IsNullOrEmpty(options.EmotionPrompt))
                {
                    payload["emotion_prompt"] = options.EmotionPrompt;
                }
            }
            else if (!string.IsNullOrEmpty(options.EmotionPrompt))
            {
                // If emotion prompt is set but UsePromptForEmotion is false,
                // we still need to enable the flag for the emotion_prompt to take effect
                payload["should_use_prompt_for_emotion"] = true;
                payload["emotion_prompt"] = options.EmotionPrompt;
            }

            // Emotion strength (only meaningful when emotion features are active)
            if (options.EmotionStrength < 1.0f)
            {
                payload["strength"] = options.EmotionStrength;
            }

            // Emotional reference audio
            if (!string.IsNullOrEmpty(options.EmotionalAudioUrl))
            {
                payload["emotional_audio_url"] = options.EmotionalAudioUrl;
            }
        }

        return payload;
    }

    private async Task<string?> CallFalApiAsync(string jsonContent)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, FalEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Key", _apiKey);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[{ProviderId}] API error {(int)response.StatusCode}: {responseBody}");
                return null;
            }

            return responseBody;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine($"[{ProviderId}] Request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[{ProviderId}] HTTP error: {ex.Message}");
            return null;
        }
    }

    private static string? ParseResponseAudioUrl(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // The FAL API returns: { "audio": { "url": "...", "content_type": "...", "file_name": "..." } }
            // or possibly just: { "audio": "https://..." }
            if (root.TryGetProperty("audio", out var audioElement))
            {
                if (audioElement.ValueKind == JsonValueKind.String)
                {
                    return audioElement.GetString();
                }
                else if (audioElement.ValueKind == JsonValueKind.Object)
                {
                    if (audioElement.TryGetProperty("url", out var urlElement))
                    {
                        return urlElement.GetString();
                    }
                }
            }

            Console.Error.WriteLine($"[indextts2-fal] Unexpected response structure: {responseJson[..Math.Min(500, responseJson.Length)]}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[indextts2-fal] Failed to parse response JSON: {ex.Message}");
            return null;
        }
    }

    private async Task<byte[]?> DownloadAudioAsync(string audioUrl)
    {
        try
        {
            var response = await _httpClient.GetAsync(audioUrl);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[{ProviderId}] Failed to download audio: {(int)response.StatusCode}");
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{ProviderId}] Download error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Decode audio bytes (mp3/wav) to raw PCM S16LE.
    /// Reads the actual sample rate and channel count from the WAV header
    /// and updates OutputSampleRate accordingly.
    /// </summary>
    private byte[]? DecodeAudioToPcm(byte[] audioBytes, string sourceUrl)
    {
        // Check if it's a WAV file (starts with RIFF header)
        if (audioBytes.Length > 44 && audioBytes[0] == 'R' && audioBytes[1] == 'I' && audioBytes[2] == 'F' && audioBytes[3] == 'F')
        {
            // Read sample rate from WAV header (offset 24, little-endian uint32)
            OutputSampleRate = BitConverter.ToInt32(audioBytes, 24);
            _detectedChannels = BitConverter.ToInt16(audioBytes, 22);
            Console.WriteLine($"[{ProviderId}] WAV detected: {OutputSampleRate} Hz, {_detectedChannels} ch");
            return ExtractPcmFromWav(audioBytes);
        }

        // Check if it's an MP3 file (starts with ID3 or 0xFF 0xFB sync)
        if (audioBytes.Length > 3 && ((audioBytes[0] == 'I' && audioBytes[1] == 'D' && audioBytes[2] == '3') ||
            (audioBytes[0] == 0xFF && (audioBytes[1] & 0xE0) == 0xE0)))
        {
            // MP3 decoding — for now, return the raw mp3 bytes and let the caller handle it.
            // TODO: Add NAudio or similar library for proper MP3→PCM decoding
            Console.WriteLine($"[{ProviderId}] Audio is MP3 format — returning raw bytes (caller must decode)");
            Console.WriteLine($"[{ProviderId}] NOTE: Add NAudio package for automatic MP3→PCM conversion");
            return audioBytes;
        }

        // Unknown format — return as-is with warning
        var ext = Path.GetExtension(new Uri(sourceUrl).AbsolutePath);
        Console.WriteLine($"[{ProviderId}] Unknown audio format ({ext}, {audioBytes.Length} bytes) — returning raw bytes");
        return audioBytes;
    }

    /// <summary>
    /// Extract raw PCM data from a WAV file, skipping the 44-byte header.
    /// Assumes standard PCM WAV format (no compression).
    /// </summary>
    private static byte[]? ExtractPcmFromWav(byte[] wavBytes)
    {
        // Find the 'data' chunk
        int dataOffset = -1;
        int dataSize = 0;

        for (int i = 12; i < wavBytes.Length - 8; i++)
        {
            if (wavBytes[i] == 'd' && wavBytes[i + 1] == 'a' && wavBytes[i + 2] == 't' && wavBytes[i + 3] == 'a')
            {
                dataSize = BitConverter.ToInt32(wavBytes, i + 4);
                dataOffset = i + 8;
                break;
            }
        }

        if (dataOffset < 0 || dataOffset + dataSize > wavBytes.Length)
        {
            // Fallback: assume standard 44-byte header
            dataOffset = 44;
            dataSize = wavBytes.Length - 44;
        }

        var pcm = new byte[dataSize];
        Buffer.BlockCopy(wavBytes, dataOffset, pcm, 0, dataSize);
        return pcm;
    }

    /// <summary>
    /// Resolve a voice reference to a URL suitable for the FAL API.
    /// Supports: HTTP(S) URLs (pass-through) and local file paths (convert to data URL).
    /// </summary>
    private async Task<string?> ResolveVoiceReferenceAsync(string voiceReference)
    {
        if (IsUrl(voiceReference))
        {
            return voiceReference;
        }

        // Local file path — read and convert to data URL
        if (File.Exists(voiceReference))
        {
            try
            {
                var fileBytes = await File.ReadAllBytesAsync(voiceReference);
                var mimeType = GetMimeTypeForAudio(voiceReference);
                var base64 = Convert.ToBase64String(fileBytes);
                var dataUrl = $"data:{mimeType};base64,{base64}";
                Console.WriteLine($"[{ProviderId}] Converted local file to data URL ({fileBytes.Length / 1024:N0} KB)");
                return dataUrl;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{ProviderId}] Failed to read voice reference file: {ex.Message}");
                return null;
            }
        }

        Console.Error.WriteLine($"[{ProviderId}] Voice reference not found: {voiceReference}");
        return null;
    }

    private static bool IsUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMimeTypeForAudio(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            ".m4a" => "audio/mp4",
            _ => "audio/mpeg"
        };
    }
}

/// <summary>
/// JSON serialization context for FAL API requests.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class FalJsonContext : JsonSerializerContext
{
}