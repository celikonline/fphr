using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;

namespace Sense.Modules.Attendance.Infrastructure.Terminals;

/// <summary>
/// <see cref="ITerminalCredentialHasher"/>: HMAC-SHA256(pepper, "{tenant:N}\n{kind}\n{normalleştirilmiş değer}") → küçük harf hex (64 karakter,
/// <see cref="EmployeeTerminalCredential.ValueMaxLength"/> içinde). Düşük entropili PIN'ler bile pepper bilinmeden çevrimdışı denenemez.
/// </summary>
public sealed class HmacTerminalCredentialHasher(IOptions<TerminalSecurityOptions> options) : ITerminalCredentialHasher
{
    private const char Separator = '\n';

    private readonly byte[] _pepper = Encoding.UTF8.GetBytes(
        options.Value.CredentialPepper?.Trim() is { Length: >= TerminalSecurityOptions.MinPepperLength } pepper
            ? pepper
            : throw new InvalidOperationException($"{TerminalSecurityOptions.Section}:{nameof(TerminalSecurityOptions.CredentialPepper)} is not configured."));

    public string Hash(Guid tenantId, TerminalCredentialKind kind, string rawValue)
    {
        ArgumentNullException.ThrowIfNull(rawValue);
        var normalized = kind == TerminalCredentialKind.Card ? rawValue.Trim().ToUpperInvariant() : rawValue.Trim();
        var message = Encoding.UTF8.GetBytes(string.Concat(tenantId.ToString("N"), Separator.ToString(), kind.ToString(), Separator.ToString(), normalized));
        return Convert.ToHexStringLower(HMACSHA256.HashData(_pepper, message));
    }
}
