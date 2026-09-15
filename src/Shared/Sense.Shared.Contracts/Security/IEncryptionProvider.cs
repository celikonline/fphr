namespace Sense.Shared.Contracts.Security;

/// <summary>Hassas alan şifreleme (TOTP secret, TCKN vb.). AES-GCM, anahtar Key Vault/user-secrets (Sense.Shared.Infrastructure.Security.AesGcmEncryptionProvider).</summary>
public interface IEncryptionProvider
{
    string Encrypt(string plaintext);

    string Decrypt(string ciphertext);
}
