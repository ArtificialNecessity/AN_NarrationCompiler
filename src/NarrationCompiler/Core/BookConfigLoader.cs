using System.Text.Json;

namespace NarrationCompiler.Core;

/// <summary>
/// Loads and validates a .mirica.metadata.jsonc config file.
/// </summary>
public static class BookConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = false
    };

    /// <summary>
    /// Load the first entry from a .mirica.metadata.jsonc file.
    /// Returns null and prints error if loading fails.
    /// </summary>
    public static BookConfig? Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"[ERROR] Config file not found: {configPath}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var entries = JsonSerializer.Deserialize<List<BookConfig>>(json, JsonOptions);

            if (entries == null || entries.Count == 0)
            {
                Console.Error.WriteLine($"[ERROR] Config file is empty or invalid: {configPath}");
                return null;
            }

            // Use the first entry
            var config = entries[0];
            Console.WriteLine($"[CONFIG] Loaded: \"{config.Title}\" ({config.Type})");
            return config;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to parse config: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve the chapters directory path relative to the config file location.
    /// </summary>
    public static string ResolveChaptersDir(BookConfig config, string configPath)
    {
        var configDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(configPath)) ?? ".";
        var chaptersPath = System.IO.Path.Combine(configDir, config.Path);
        return System.IO.Path.GetFullPath(chaptersPath);
    }
}