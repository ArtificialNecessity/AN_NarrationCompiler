using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AstroCryptKit;

/// <summary>
/// Wrapper for sensitive values that prevents accidental exposure in logs
/// ToString() automatically elides 80% of the value
/// </summary>
public readonly struct SecureKey
{
    private readonly string _value;

    public SecureKey(string value) => _value = value;

    /// <summary>
    /// Gets the actual unmasked value - use with caution!
    /// </summary>
    public string RevealDangerouslySecretKeyValue() => _value;

    /// <summary>
    /// Returns masked representation: shows ~20% of characters, hides 80%
    /// Example: "abc...***********" for "abcdefghijklmnop"
    /// </summary>
    public override string ToString()
    {
        if (string.IsNullOrEmpty(_value)) return "(empty)";
        if (_value.Length <= 4) return "****";

        int visibleChars = Math.Max(2, _value.Length / 5); // Show ~20%
        return _value.Substring(0, visibleChars) + "..." +
               new string('*', _value.Length - visibleChars);
    }
}

/// <summary>
/// AstroCrypt - Secure encryption/decryption for API keys and secrets
/// Uses AES-256-CBC with PBKDF2 key derivation
/// </summary>
public static class AstroCrypt
{
    /// <summary>
    /// Encrypts plaintext using AES with password-derived key
    /// </summary>
    /// <param name="plainText">The text to encrypt</param>
    /// <param name="password">User password for encryption</param>
    /// <returns>Base64-encoded encrypted data with embedded salt</returns>
    public static string Encrypt(string plainText, string password)
    {
        // Generate salt for key derivation
        byte[] salt = GenerateSalt();

        // Derive key and IV from password using PBKDF2
        using (var keyDerivation = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256))
        {
            byte[] keyBytes = keyDerivation.GetBytes(32); // 256-bit key
            byte[] ivBytes = keyDerivation.GetBytes(16);  // 128-bit IV

            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                    byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                    // Combine salt + encrypted data
                    byte[] result = new byte[salt.Length + cipherBytes.Length];
                    Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
                    Buffer.BlockCopy(cipherBytes, 0, result, salt.Length, cipherBytes.Length);

                    // Return as base64
                    return Convert.ToBase64String(result);
                }
            }
        }
    }

    /// <summary>
    /// Decrypts ciphertext using AES with password-derived key
    /// </summary>
    /// <param name="cipherText">Base64-encoded encrypted data</param>
    /// <param name="password">User password for decryption</param>
    /// <returns>SecureKey wrapper with elided ToString()</returns>
    public static SecureKey Decrypt(string cipherText, string password)
    {
        byte[] combined = Convert.FromBase64String(cipherText);

        // Extract salt (first 16 bytes)
        byte[] salt = new byte[16];
        Buffer.BlockCopy(combined, 0, salt, 0, 16);

        // Extract encrypted data
        byte[] cipherBytes = new byte[combined.Length - 16];
        Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherBytes.Length);

        // Derive key and IV from password using PBKDF2
        using (var keyDerivation = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256))
        {
            byte[] keyBytes = keyDerivation.GetBytes(32); // 256-bit key
            byte[] ivBytes = keyDerivation.GetBytes(16);  // 128-bit IV

            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    string decryptedValue = Encoding.UTF8.GetString(plainBytes);
                    return new SecureKey(decryptedValue);
                }
            }
        }
    }

    /// <summary>
    /// Prompts for password with asterisk masking
    /// </summary>
    /// <param name="promptText">Text to display before password input</param>
    /// <returns>The entered password</returns>
    public static string PromptForPassword(string promptText)
    {
        Console.Write(promptText);
        StringBuilder password = new StringBuilder();
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        } while (true);

        return password.ToString();
    }

    /// <summary>
    /// Generates a random salt for key derivation
    /// </summary>
    private static byte[] GenerateSalt()
    {
        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return salt;
    }
}