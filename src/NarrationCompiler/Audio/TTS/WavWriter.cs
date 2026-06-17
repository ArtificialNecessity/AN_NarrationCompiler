namespace NarrationCompiler.Utils;

/// <summary>
/// Writes raw PCM S16LE data into a standard WAV file.
/// </summary>
public static class WavWriter
{
    /// <summary>
    /// Write PCM S16LE byte data to a WAV file.
    /// </summary>
    public static void WritePcmToWav(string outputPath, byte[] pcmData, int sampleRate, int channels = 1)
    {
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(fs);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write((int)(36 + pcmData.Length)); // file size - 8
        writer.Write("WAVE"u8);

        // fmt sub-chunk
        writer.Write("fmt "u8);
        writer.Write(16);                       // sub-chunk size (PCM = 16)
        writer.Write((short)1);                 // audio format (1 = PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);

        // data sub-chunk
        writer.Write("data"u8);
        writer.Write(pcmData.Length);
        writer.Write(pcmData);
    }
}