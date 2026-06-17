#!/usr/bin/env dotnet run
// Description: Generate Cartesia voice reference WAV files using a known phrase.
// Usage: dotnet run Scripts/GenerateVoiceReference.cs
//   - Prompts for AstroCrypt password (once), then loops asking for voice ID + name.
//   - Renders "Sunset, and evening star, and one clear call for me." via Cartesia TTS.
//   - Saves to Assets/speech_reference/{name}.wav
//   - Press Enter with empty voice ID to exit.

#:project ../src/NarrationCompiler/NarrationCompiler.csproj

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AstroCryptKit;
using NarrationCompiler;
using NarrationCompiler.Utils;

// ─── Configuration ───────────────────────────────────────────────────────────

const string REFERENCE_PHRASE = "Sunset, and evening star, and one clear call for me.";
const string CARTESIA_KEY_NAME = "cartesia_api_key";
const int SAMPLE_RATE = 44100;
const string OUTPUT_DIR = "Assets/speech_reference";
var jsonOptions = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

// ─── Keystore unlock ─────────────────────────────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Cartesia Voice Reference Generator");
Console.WriteLine("  Phrase: \"" + REFERENCE_PHRASE + "\"");
Console.WriteLine("  (Enter empty Voice ID to quit)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();

var keystore = KeystoreLoader.LoadAndUnlock();
if (keystore is null)
{
    Console.Error.WriteLine("[FATAL] Could not unlock keystore. Exiting.");
    return;
}

if (!keystore.HasKey(CARTESIA_KEY_NAME))
{
    Console.Error.WriteLine($"[ERROR] Key '{CARTESIA_KEY_NAME}' not found in keystore.");
    Console.Error.WriteLine($"  Available keys: [{string.Join(", ", keystore.KeyNames)}]");
    return;
}

string apiKey = keystore.RevealDangerouslySecretKeyValue(CARTESIA_KEY_NAME);
Console.WriteLine($"[OK] Cartesia API key loaded.");

// Ensure output directory exists
Directory.CreateDirectory(OUTPUT_DIR);

// ─── Main loop ──────────────────────────────────────────────────────────────

int generatedCount = 0;

while (true)
{
    Console.WriteLine();
    Console.WriteLine("───────────────────────────────────────────────────────");
    Console.Write("Enter Cartesia Voice ID (or Enter to quit): ");
    string? voiceId = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(voiceId))
    {
        break;
    }

    Console.Write("Enter name for the reference file (no extension): ");
    string? fileName = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(fileName))
    {
        Console.Error.WriteLine("[ERROR] File name cannot be empty. Skipping.");
        continue;
    }

    // Sanitize filename: replace spaces with underscores, remove unsafe chars
    fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    fileName = fileName.Replace(' ', '_');

    string outputPath = Path.Combine(OUTPUT_DIR, fileName + ".wav");

    if (File.Exists(outputPath))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠ File already exists: {outputPath}");
        Console.ResetColor();
        Console.Write("Overwrite? (y/N): ");
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("Skipped.");
            continue;
        }
    }

    // ─── Synthesize via Cartesia WebSocket ───────────────────────────────

    Console.WriteLine();
    Console.WriteLine($"\ud83c\udfa4 Synthesizing with voice ID: {voiceId}");
    Console.WriteLine($"\ud83d\udcdd Text: \"{REFERENCE_PHRASE}\"");
    Console.WriteLine($"\ud83d\udcbe Output: {outputPath}");
    Console.WriteLine();

    var pcmChunks = new List<byte[]>();
    var completionTcs = new TaskCompletionSource();
    Exception? synthesisError = null;

    using var ws = new ClientWebSocket();
    var wsUri = new Uri($"wss://api.cartesia.ai/tts/websocket?api_key={apiKey}&cartesia_version=2024-06-10");

    Console.WriteLine("\ud83d\udd0c Connecting to Cartesia WebSocket...");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

    try
    {
        await ws.ConnectAsync(wsUri, cts.Token);
        Console.WriteLine("\u2705 Connected.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[ERROR] WebSocket connection failed: {ex.Message}");
        continue;
    }

    // Context ID for this synthesis turn
    string contextId = $"ctx_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..32];

    // Start receive loop in background
    var receiveTask = Task.Run(async () =>
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var type = root.GetProperty("type").GetString();

                    switch (type)
                    {
                        case "chunk":
                            if (root.TryGetProperty("data", out var dataEl))
                            {
                                var b64 = dataEl.GetString();
                                if (!string.IsNullOrEmpty(b64))
                                {
                                    pcmChunks.Add(Convert.FromBase64String(b64));
                                }
                            }
                            break;

                        case "done":
                            completionTcs.TrySetResult();
                            return;

                        case "error":
                            var msg = root.TryGetProperty("message", out var msgEl)
                                ? msgEl.GetString() ?? "Unknown error"
                                : "Unknown Cartesia error";
                            synthesisError = new Exception($"Cartesia TTS error: {msg}");
                            completionTcs.TrySetResult();
                            return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            completionTcs.TrySetResult();
        }
    });

    // Send the synthesis request (single message, continue=false to get all audio)
    var payload = new
    {
        model_id = "sonic-3",
        transcript = REFERENCE_PHRASE,
        voice = new { mode = "id", id = voiceId },
        context_id = contextId,
        @continue = false,
        output_format = new
        {
            container = "raw",
            encoding = "pcm_s16le",
            sample_rate = SAMPLE_RATE
        }
    };

    var payloadJson = JsonSerializer.Serialize(payload, jsonOptions);
    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

    Console.WriteLine("\ud83d\udce1 Sending synthesis request...");
    await ws.SendAsync(payloadBytes, WebSocketMessageType.Text, true, cts.Token);

    // Wait for completion
    await completionTcs.Task;

    // Clean up WebSocket
    if (ws.State == WebSocketState.Open)
    {
        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
        catch { /* best-effort */ }
    }

    if (synthesisError is not null)
    {
        Console.Error.WriteLine($"[ERROR] Synthesis failed: {synthesisError.Message}");
        continue;
    }

    if (pcmChunks.Count == 0)
    {
        Console.Error.WriteLine("[ERROR] No audio data received from Cartesia.");
        continue;
    }

    // ─── Write WAV file ──────────────────────────────────────────────────

    // Combine all PCM chunks
    int totalBytes = pcmChunks.Sum(c => c.Length);
    var pcmData = new byte[totalBytes];
    int offset = 0;
    foreach (var chunk in pcmChunks)
    {
        Buffer.BlockCopy(chunk, 0, pcmData, offset, chunk.Length);
        offset += chunk.Length;
    }

    // Write WAV
    WavWriter.WritePcmToWav(outputPath, pcmData, SAMPLE_RATE, channels: 1);

    double durationSeconds = (double)pcmData.Length / (SAMPLE_RATE * 2); // 16-bit = 2 bytes/sample
    generatedCount++;
    Console.WriteLine();
    Console.WriteLine($"\u2705 Voice reference saved!");
    Console.WriteLine($"   File: {outputPath}");
    Console.WriteLine($"   Size: {pcmData.Length:N0} bytes ({durationSeconds:F1}s)");
    Console.WriteLine($"   Format: WAV PCM S16LE, {SAMPLE_RATE} Hz, mono");
    Console.WriteLine($"   Voice ID: {voiceId}");
}

// ─── Done ────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine($"Done. Generated {generatedCount} voice reference(s).");