using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>
/// Terminal API anahtarı (Z5.1): 32 bayt kriptografik rastgele değer, base64url (dolgusuz, 43 karakter). Veritabanına yalnız SHA-256
/// özeti (küçük harf hex) ve arama için ilk <see cref="PrefixLength"/> karakter (gizli değil, 48 bit) yazılır; ham anahtar yalnız
/// üretim/yenileme yanıtında bir kez döner. Anahtar yüksek entropili olduğu için tuz/yavaş özet gerekmez (parola değildir).
/// </summary>
public static class TerminalApiKey
{
    public const int KeyBytes = 32;
    public const int PrefixLength = 8;

    /// <summary>32 baytın dolgusuz base64url uzunluğu.</summary>
    public const int EncodedLength = 43;

    private const int DigestBytes = 32;

    /// <summary>Yeni anahtar üretir: ham değer + önek + özet.</summary>
    public static TerminalApiKeyMaterial Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeyBytes);
        try
        {
            var key = Base64Url.EncodeToString(bytes);
            return new TerminalApiKeyMaterial(key, PrefixOf(key), Hash(key));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Biçim denetimi (uzunluk + base64url alfabesi). Biçim gizli değildir; bozuk anahtar veritabanına sorgu açmaz.</summary>
    public static bool IsWellFormed(string? key) => key is { Length: EncodedLength } && key.All(IsBase64UrlChar);

    public static string PrefixOf(string key) => key[..PrefixLength];

    /// <summary>Saklanan biçim: SHA-256 (UTF-8) küçük harf hex.</summary>
    public static string Hash(string key) => Convert.ToHexStringLower(Digest(key));

    internal static byte[] Digest(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>Saklanan özeti karşılaştırma için bayta çevirir; bozuk kayıt eşleşmeyen sabit uzunlukta bir değere düşer (istisna yok, süre farkı yok).</summary>
    internal static byte[] StoredDigest(string storedHash)
    {
        if (storedHash.Length == DigestBytes * 2)
        {
            try
            {
                return Convert.FromHexString(storedHash);
            }
            catch (FormatException)
            {
                // bozuk kayıt: aşağıdaki eşleşmeyen değer
            }
        }

        return new byte[DigestBytes];
    }

    private static bool IsBase64UrlChar(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}

/// <summary>Yeni anahtarın üç parçası. <see cref="ToString"/> ham anahtarı yazmaz (yanlışlıkla loglanmasın).</summary>
public sealed record TerminalApiKeyMaterial(string Key, string Prefix, string Hash)
{
    public override string ToString() => $"{nameof(TerminalApiKeyMaterial)} {{ {nameof(Prefix)} = {Prefix} }}";
}
