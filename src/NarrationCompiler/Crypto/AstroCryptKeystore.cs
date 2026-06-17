using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AstroCryptKit;

/// <summary>
/// Manages a registry of encrypted key-value pairs, unlocked by a user passphrase.
/// Keys are stored encrypted on disk (JSON) and decrypted on-demand in memory.
/// 
/// Usage:
///   var keystore = AstroCryptKeystore.CreateNew(passphrase);
///   keystore.SetKeyFromPlaintext("openrouter_api_key", "sk-or-...");
///   keystore.SaveToFile("~/.mirica-llm/EncryptedKeys.json");
///   
///   // Later:
///   var keystore = AstroCryptKeystore.LoadFromFile(path, passphrase);
///   if (!keystore.ValidatePassphrase()) { /* wrong password */ }
///   string apiKey = keystore.RevealDangerouslySecretKeyValue("openrouter_api_key");
/// </summary>
public class AstroCryptKeystore
{
    /// <summary>
    /// The key name used to store the passphrase validation check value.
    /// </summary>
    private const string PassphraseValidationKeyName = "user_password";

    /// <summary>
    /// The expected plaintext value stored under the validation key.
    /// If decryption of this key yields this value, the passphrase is correct.
    /// </summary>
    private const string PassphraseValidationExpectedPlaintext = "valid_password";

    private readonly string _userPassphrase;
    private readonly Dictionary<string, string> _encryptedKeysByName;

    /// <summary>
    /// Shared JsonSerializerOptions with reflection-based resolver enabled.
    /// Required for .NET 10+ where reflection serialization is opt-in.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true
    };

    private AstroCryptKeystore(string userPassphrase, Dictionary<string, string> encryptedKeysByName)
    {
        _userPassphrase = userPassphrase;
        _encryptedKeysByName = encryptedKeysByName;
    }

    /// <summary>
    /// Creates a brand-new keystore with the given passphrase.
    /// Automatically stores the passphrase validation entry.
    /// </summary>
    public static AstroCryptKeystore CreateNew(string userPassphrase)
    {
        var keystore = new AstroCryptKeystore(userPassphrase, new Dictionary<string, string>());
        keystore.SetKeyFromPlaintext(PassphraseValidationKeyName, PassphraseValidationExpectedPlaintext);
        return keystore;
    }

    /// <summary>
    /// Loads an existing keystore from a JSON file.
    /// Does NOT validate the passphrase — call ValidatePassphrase() after loading.
    /// </summary>
    public static AstroCryptKeystore LoadFromFile(string keystoreFilePath, string userPassphrase)
    {
        string keystoreJsonContent = File.ReadAllText(keystoreFilePath);
        Dictionary<string, string> encryptedKeysByName =
            JsonSerializer.Deserialize<Dictionary<string, string>>(keystoreJsonContent, s_jsonOptions)
            ?? throw new InvalidOperationException(
                $"AstroCryptKeystore.LoadFromFile: Failed to deserialize keystore at '{keystoreFilePath}'. " +
                "File may be corrupted. Expected JSON object with string key-value pairs.");
        return new AstroCryptKeystore(userPassphrase, encryptedKeysByName);
    }

    /// <summary>
    /// Saves the keystore to a JSON file. Creates parent directories if needed.
    /// </summary>
    public void SaveToFile(string keystoreFilePath)
    {
        string? keystoreDirectoryPath = Path.GetDirectoryName(keystoreFilePath);
        if (keystoreDirectoryPath != null) {
            Directory.CreateDirectory(keystoreDirectoryPath);
        }

        string keystoreJsonContent = JsonSerializer.Serialize(
            _encryptedKeysByName, s_jsonOptions);
        File.WriteAllText(keystoreFilePath, keystoreJsonContent);
    }

    /// <summary>
    /// Validates that the passphrase is correct by decrypting the validation key.
    /// Returns true if the passphrase matches, false if decryption fails or value doesn't match.
    /// </summary>
    public bool ValidatePassphrase()
    {
        if (!_encryptedKeysByName.ContainsKey(PassphraseValidationKeyName)) {
            return false;
        }

        try {
            SecureKey decryptedValidationValue = AstroCrypt.Decrypt(
                _encryptedKeysByName[PassphraseValidationKeyName],
                _userPassphrase);
            return decryptedValidationValue.RevealDangerouslySecretKeyValue() == PassphraseValidationExpectedPlaintext;
        }
        catch (System.Security.Cryptography.CryptographicException) {
            // Wrong passphrase causes decryption to fail with bad padding
            return false;
        }
    }

    /// <summary>
    /// Stores an already-encrypted value under the given key name.
    /// </summary>
    public void SetEncryptedKey(string keyName, string encryptedKeyValue)
    {
        _encryptedKeysByName[keyName] = encryptedKeyValue;
    }

    /// <summary>
    /// Encrypts a plaintext value with the user's passphrase and stores it.
    /// </summary>
    public void SetKeyFromPlaintext(string keyName, string plaintextKeyValue)
    {
        string encryptedKeyValue = AstroCrypt.Encrypt(plaintextKeyValue, _userPassphrase);
        _encryptedKeysByName[keyName] = encryptedKeyValue;
    }

    /// <summary>
    /// Decrypts and returns the plaintext value for the given key name.
    /// Use sparingly — the returned string is the actual secret.
    /// </summary>
    public string RevealDangerouslySecretKeyValue(string keyName)
    {
        if (!_encryptedKeysByName.TryGetValue(keyName, out string? encryptedKeyValue)) {
            throw new KeyNotFoundException(
                $"AstroCryptKeystore.RevealDangerouslySecretKeyValue: No key named '{keyName}' in keystore. " +
                $"Available keys: [{string.Join(", ", _encryptedKeysByName.Keys)}]");
        }

        SecureKey decryptedValue = AstroCrypt.Decrypt(encryptedKeyValue, _userPassphrase);
        return decryptedValue.RevealDangerouslySecretKeyValue();
    }

    /// <summary>
    /// Returns true if the keystore contains a key with the given name.
    /// </summary>
    public bool HasKey(string keyName)
    {
        return _encryptedKeysByName.ContainsKey(keyName);
    }

    /// <summary>
    /// Returns the names of all keys in the keystore (does not reveal values).
    /// </summary>
    public IReadOnlyCollection<string> KeyNames => _encryptedKeysByName.Keys;
}