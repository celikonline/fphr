namespace Sense.Modules.Identity.Application;

/// <summary>
/// Parola gücü kuralı (uzunluk kuralı <see cref="IdentityOptions.MinPasswordLength"/> ayrıdır): en az 3 karakter sınıfı
/// (büyük/küçük harf, rakam, sembol), tek karakterden ibaret olmama, bilinen yaygın parolaları ve e-postanın yerel kısmını
/// içermeme. Davet kabulü, parola sıfırlama ve kiracı kaydı aynı kuralı kullanır.
/// </summary>
public static class PasswordPolicy
{
    private const int MinCharacterClasses = 3;
    private const int MinEmailLocalPartLength = 3;

    private static readonly string[] CommonPasswords =
    [
        "password", "passw0rd", "p@ssw0rd", "p@ssword", "12345678", "123456789", "qwerty", "asdfgh",
        "demo123", "admin123", "letmein", "welcome", "iloveyou", "abc12345", "senseik",
    ];

    /// <summary>E-postadan bağımsız kurallar; e-posta yerel kısmı kontrolü <see cref="ContainsEmailLocalPart"/> ile ayrıca yapılır (token akışlarında e-posta handler içinde bilinir).</summary>
    public static bool IsStrongEnough(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Distinct().Count() < 2)
        {
            return false;
        }

        var lower = password.ToLowerInvariant();
        if (CommonPasswords.Any(lower.Contains))
        {
            return false;
        }

        var classes = 0;
        classes += password.Any(char.IsUpper) ? 1 : 0;
        classes += password.Any(char.IsLower) ? 1 : 0;
        classes += password.Any(char.IsDigit) ? 1 : 0;
        classes += password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0;
        return classes >= MinCharacterClasses;
    }

    /// <summary>Parola, e-postanın yerel kısmını (@ öncesi, en az 3 karakter) içeriyorsa true.</summary>
    public static bool ContainsEmailLocalPart(string password, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        var local = (at > 0 ? email[..at] : email).Trim();
        return local.Length >= MinEmailLocalPartLength
            && password.Contains(local, StringComparison.OrdinalIgnoreCase);
    }
}
