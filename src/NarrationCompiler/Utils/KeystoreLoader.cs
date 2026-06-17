using AstroCryptKit;

namespace NarrationCompiler;

/// <summary>
/// Loads the AstroCrypt keystore from the standard platform location,
/// prompts for password, validates, and provides key access.
/// </summary>
public static class KeystoreLoader
{
    /// <summary>
    /// Standard keystore path: {LocalAppData}/ArtificialNecessity/MiricaLLMData/EncryptedKeys.json
    /// </summary>
    public static string GetKeystorePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ArtificialNecessity", "MiricaLLMData", "EncryptedKeys.json");
    }

    /// <summary>
    /// Load keystore, prompt for password, validate. Returns null on failure.
    /// </summary>
    public static AstroCryptKeystore? LoadAndUnlock()
    {
        var keystorePath = GetKeystorePath();

        if (!File.Exists(keystorePath))
        {
            Console.Error.WriteLine($"[ERROR] Keystore not found: {keystorePath}");
            return null;
        }

        Console.WriteLine($"Keystore: {keystorePath}");
        var password = AstroCrypt.PromptForPassword("Enter decryption password: ");

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("[ERROR] Empty password.");
            return null;
        }

        var keystore = AstroCryptKeystore.LoadFromFile(keystorePath, password);

        if (!keystore.ValidatePassphrase())
        {
            Console.Error.WriteLine("[ERROR] Invalid password - decryption failed.");
            return null;
        }

        Console.WriteLine("[OK] Keystore unlocked.");
        return keystore;
    }
}