using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Claims.Infrastructure.Security;

/// <summary>Settings for encrypting personal data at rest (section <c>Encryption</c>).</summary>
public sealed class EncryptionOptions
{
    /// <summary>Base64 encoded 256-bit key. Keep it in a secret store such as Azure Key Vault, never in source control.</summary>
    public string Key { get; set; } = "";
}

/// <summary>Requires a valid key, except in Development where a throw-away key is generated.</summary>
public sealed class EncryptionOptionsValidator(IHostEnvironment environment) : IValidateOptions<EncryptionOptions>
{
    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key))
            return environment.IsDevelopment()
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Encryption:Key is required.");

        return AesGcmFieldEncryptor.TryParseKey(options.Key, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Encryption:Key must be a base64 encoded 32 byte key.");
    }
}

/// <summary>Encrypts and decrypts single text fields before they are stored.</summary>
public interface IFieldEncryptor
{
    string Encrypt(string plainText);

    string Decrypt(string stored);
}

/// <summary>
/// AES-256-GCM encryption. Every value gets a fresh random nonce, and the authentication tag makes tampering
/// detectable. The stored form is <c>v1:</c> followed by base64 of nonce, tag and cipher text; the version prefix
/// leaves room for key rotation.
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const string Prefix = "v1:";
    private const int KeySize = 32, NonceSize = 12, TagSize = 16;

    private readonly byte[] _key;

    public AesGcmFieldEncryptor(byte[] key) =>
        _key = key.Length == KeySize ? key : throw new ArgumentException("The key must be 32 bytes.", nameof(key));

    /// <summary>A throw-away key for Development, where the local databases do not outlive the process either.</summary>
    public static AesGcmFieldEncryptor CreateEphemeral() => new(RandomNumberGenerator.GetBytes(KeySize));

    /// <summary>Parses a base64 key, returning <c>false</c> if it is not exactly 32 bytes.</summary>
    public static bool TryParseKey(string base64, out byte[] key)
    {
        key = new byte[KeySize];
        return Convert.TryFromBase64String(base64, key, out var written) && written == KeySize;
    }

    public string Encrypt(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var payload = new byte[NonceSize + TagSize + plain.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, payload.AsSpan(NonceSize + TagSize), tag);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("The stored value is not encrypted with a known format.");

        var payload = Convert.FromBase64String(stored[Prefix.Length..]);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), cipher, payload.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
