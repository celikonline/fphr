using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sense.Shared.Contracts.Security;

namespace Sense.Shared.Infrastructure.Security;

/// <summary>AES-GCM alan şifreleme. Anahtar: Security:EncryptionKey (base64, 32 byte). Geliştirmede yoksa deterministik türetilir.</summary>
public sealed class AesGcmEncryptionProvider(IOptions<SecurityOptions> options) : IEncryptionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key = ResolveKey(options.Value);

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Decrypt(string ciphertext)
    {
        var all = Convert.FromBase64String(ciphertext);
        var nonce = all.AsSpan(0, NonceSize);
        var tag = all.AsSpan(NonceSize, TagSize);
        var cipher = all.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] ResolveKey(SecurityOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.EncryptionKey))
        {
            return Convert.FromBase64String(o.EncryptionKey);
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(o.DevelopmentKeySeed));
    }
}

public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>Base64 kodlu 32 byte AES anahtarı (prod: Key Vault).</summary>
    public string? EncryptionKey { get; set; }

    public string DevelopmentKeySeed { get; set; } = SecurityDefaults.DevelopmentKeySeed;
}

public static class SecurityDefaults
{
    public const string DevelopmentKeySeed = "senseik-development-only-key";
}
