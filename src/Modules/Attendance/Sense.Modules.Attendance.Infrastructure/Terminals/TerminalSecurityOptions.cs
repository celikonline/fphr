using Microsoft.Extensions.Options;

namespace Sense.Modules.Attendance.Infrastructure.Terminals;

/// <summary>
/// Terminal güvenlik ayarları (yapılandırma bölümü <see cref="Section"/>). <see cref="CredentialPepper"/> GİZLİDİR: appsettings'e yazılmaz;
/// ortam değişkeni <c>Attendance__Terminals__CredentialPepper</c>, user-secrets ya da gizli anahtar kasası ile verilir. Değiştirilirse
/// saklanan tüm kimlik özetleri geçersizleşir (kimlikler yeniden tanımlanmalıdır).
/// </summary>
public sealed class TerminalSecurityOptions
{
    public const string Section = "Attendance:Terminals";

    /// <summary>En az bu kadar karakter (ör. <c>openssl rand -base64 48</c>).</summary>
    public const int MinPepperLength = 32;

    public string? CredentialPepper { get; set; }
}

/// <summary>Başlangıçta (ValidateOnStart) anlaşılır hata: pepper yok ya da kısa.</summary>
public sealed class TerminalSecurityOptionsValidator : IValidateOptions<TerminalSecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, TerminalSecurityOptions options) =>
        options.CredentialPepper is { } pepper && pepper.Trim().Length >= TerminalSecurityOptions.MinPepperLength
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{TerminalSecurityOptions.Section}:{nameof(TerminalSecurityOptions.CredentialPepper)}' yapılandırılmamış ya da {TerminalSecurityOptions.MinPepperLength} " +
                "karakterden kısa. Terminal kimlik (kart/PIN) özetleri için gizli bir değer verin: ortam değişkeni " +
                "Attendance__Terminals__CredentialPepper (docker: ATTENDANCE_TERMINAL_CREDENTIAL_PEPPER), user-secrets ya da anahtar kasası. " +
                "appsettings dosyasına yazmayın.");
}
