using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Infrastructure.Terminals;

/// <summary>
/// <see cref="ITerminalCredentialHasher"/>: HMAC-SHA256(pepper, "{tenant:N}\n{kind}\n{normalleştirilmiş değer}") → küçük harf hex (64 karakter,
/// <see cref="EmployeeTerminalCredential.ValueMaxLength"/> içinde). Düşük entropili PIN'ler bile pepper bilinmeden çevrimdışı denenemez.
/// Pepper yoksa oluşturma başarılı olur (API açılır); <see cref="Hash"/> 503 hatası döner.
/// </summary>
public sealed class HmacTerminalCredentialHasher(IOptions<TerminalSecurityOptions> options) : ITerminalCredentialHasher
{
    private const char Separator = '\n';

    private readonly byte[]? _pepper = options.Value.UsablePepper is { } pepper ? Encoding.UTF8.GetBytes(pepper) : null;

    public bool IsAvailable => _pepper is not null;

    public Result<string> Hash(Guid tenantId, TerminalCredentialKind kind, string rawValue)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        if (_pepper is null)
        {
            return Error.ServiceUnavailable(TerminalErrors.CredentialPepperMissing);
        }

        var normalized = kind == TerminalCredentialKind.Card ? rawValue.Trim().ToUpperInvariant() : rawValue.Trim();
        var message = Encoding.UTF8.GetBytes(string.Concat(tenantId.ToString("N"), Separator.ToString(), kind.ToString(), Separator.ToString(), normalized));
        return Convert.ToHexStringLower(HMACSHA256.HashData(_pepper, message));
    }
}
