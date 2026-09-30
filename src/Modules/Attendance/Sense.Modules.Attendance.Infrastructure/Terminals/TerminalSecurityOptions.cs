using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sense.Modules.Attendance.Infrastructure.Terminals;

/// <summary>
/// Terminal güvenlik ayarları (yapılandırma bölümü <see cref="Section"/>). <see cref="CredentialPepper"/> GİZLİDİR: appsettings'e yazılmaz;
/// ortam değişkeni <c>Attendance__Terminals__CredentialPepper</c>, user-secrets ya da gizli anahtar kasası ile verilir. Zorunlu DEĞİLDİR: yoksa
/// API açılır, başlangıçta uyarı loglanır ve yalnız kimlik (kart/PIN) işlemleri 503 döner. Değiştirilirse saklanan tüm kimlik özetleri
/// geçersizleşir (kimlikler yeniden tanımlanmalıdır).
/// </summary>
public sealed class TerminalSecurityOptions
{
    public const string Section = "Attendance:Terminals";

    /// <summary>En az bu kadar karakter (ör. <c>openssl rand -base64 48</c>).</summary>
    public const int MinPepperLength = 32;

    public string? CredentialPepper { get; set; }

    /// <summary>Kullanılabilir pepper (kırpılmış, yeterli uzunlukta) ya da null.</summary>
    public string? UsablePepper => CredentialPepper?.Trim() is { Length: >= MinPepperLength } pepper ? pepper : null;
}

/// <summary>
/// Başlangıç denetimi: pepper yoksa/kısaysa API'yi DURDURMAZ, yalnız uyarı loglar (değer hiçbir koşulda loglanmaz).
/// </summary>
public sealed partial class TerminalSecurityStartupCheck(IOptions<TerminalSecurityOptions> options, ILogger<TerminalSecurityStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.UsablePepper is null)
        {
            LogPepperMissing(logger, $"{TerminalSecurityOptions.Section}:{nameof(TerminalSecurityOptions.CredentialPepper)}", TerminalSecurityOptions.MinPepperLength);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning,
        Message = "{ConfigKey} is not configured or shorter than {MinLength} characters: terminal credential (card/PIN) operations will return 503 until it is set (env Attendance__Terminals__CredentialPepper).")]
    private static partial void LogPepperMissing(ILogger logger, string configKey, int minLength);
}
