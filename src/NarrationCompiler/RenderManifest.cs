using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NarrationCompiler;

/// <summary>
/// Tracks content hashes for rendered chapter audio files.
/// Used to skip re-rendering when prose + voice haven't changed.
/// Stored as render_manifest.json in the output directory.
/// </summary>
public class RenderManifest
{
    /// <summary>
    /// Map of output filename -> content hash (SHA256 of prose + voiceId)
    /// </summary>
    public Dictionary<string, string> Hashes { get; set; } = new();

    private string? _manifestPath;

    /// <summary>
    /// Load manifest from an output directory (or create empty if not found).
    /// </summary>
    public static RenderManifest Load(string outputDir)
    {
        var path = Path.Combine(outputDir, "render_manifest.json");
        RenderManifest manifest;

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            manifest = JsonSerializer.Deserialize<RenderManifest>(json) ?? new RenderManifest();
        }
        else
        {
            manifest = new RenderManifest();
        }

        manifest._manifestPath = path;
        return manifest;
    }

    /// <summary>
    /// Compute the content hash for a chapter (prose text + voice ID).
    /// </summary>
    public static string ComputeHash(string proseContent, string voiceId)
    {
        var input = $"{voiceId}\n{proseContent}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..16]; // 16 hex chars is plenty
    }

    /// <summary>
    /// Check if a chapter needs re-rendering.
    /// Returns true if the file doesn't exist or the hash has changed.
    /// </summary>
    public bool NeedsRender(string outputFileName, string currentHash)
    {
        if (!Hashes.TryGetValue(outputFileName, out var storedHash))
            return true;

        if (storedHash != currentHash)
            return true;

        // Hash matches — but also check that the file actually exists
        if (_manifestPath != null)
        {
            var outputDir = Path.GetDirectoryName(_manifestPath)!;
            var filePath = Path.Combine(outputDir, outputFileName);
            if (!File.Exists(filePath))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Record a successful render.
    /// </summary>
    public void RecordRender(string outputFileName, string hash)
    {
        Hashes[outputFileName] = hash;
    }

    /// <summary>
    /// Save manifest to disk.
    /// </summary>
    public void Save()
    {
        if (_manifestPath == null) return;

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_manifestPath, json);
    }
}