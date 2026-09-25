using System.Security.Cryptography;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Kimliği doğrulanmış terminal: kiracı yalnız buradan (eşleşen aktif terminalden) gelir, istek gövdesinden asla.</summary>
public sealed record TerminalIdentity(Guid TerminalId, Guid TenantId, TerminalDirectionMode DirectionMode, Guid? LocationId);

/// <summary>Okutma ucu kimlik doğrulaması (X-Terminal-Key): ham anahtar → eşleşen aktif terminal ya da null.</summary>
public interface ITerminalKeyAuthenticator
{
    Task<TerminalIdentity?> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken);
}

/// <summary>
/// Önek araması kiracılar arasıdır ve birden çok aday döndürebilir: HER aday için tam özet <see cref="CryptographicOperations.FixedTimeEquals"/>
/// ile karşılaştırılır (ilk eşleşmede döngüden çıkılmaz); aday yoksa sahte bir özetle yine bir karşılaştırma yapılır. Kiracı yalnız eşleşen
/// VE aktif terminalden alınır. Ham anahtar hiçbir yere yazılmaz/loglanmaz.
/// </summary>
public sealed class TerminalKeyAuthenticator(ITerminalRepository terminals) : ITerminalKeyAuthenticator
{
    private static readonly byte[] DecoyDigest = TerminalApiKey.Digest(new string('0', TerminalApiKey.EncodedLength));

    public async Task<TerminalIdentity?> AuthenticateAsync(string? presentedKey, CancellationToken cancellationToken)
    {
        if (!TerminalApiKey.IsWellFormed(presentedKey))
        {
            return null;
        }

        var presented = TerminalApiKey.Digest(presentedKey!);
        var candidates = await terminals.ListActiveByApiKeyPrefixAcrossTenantsAsync(TerminalApiKey.PrefixOf(presentedKey!), cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            _ = CryptographicOperations.FixedTimeEquals(presented, DecoyDigest);
            return null;
        }

        Terminal? match = null;
        foreach (var candidate in candidates)
        {
            var equal = CryptographicOperations.FixedTimeEquals(presented, TerminalApiKey.StoredDigest(candidate.ApiKeyHash));
            if (equal && match is null)
            {
                match = candidate;
            }
        }

        return match is { IsActive: true }
            ? new TerminalIdentity(match.Id, match.TenantId, match.DirectionMode, match.LocationId)
            : null;
    }
}
