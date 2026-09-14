using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Identity.Domain.Tokens;

/// <summary>
/// Dönen (rotating) refresh token. Her kullanımda yenisi verilir, eskisi ReplacedByTokenHash ile işaretlenir.
/// İptal edilmiş bir token yeniden kullanılırsa aynı FamilyId'deki tüm token'lar iptal edilir (reuse detection).
/// </summary>
public sealed class RefreshToken : TenantEntity<Guid>
{
    private RefreshToken()
    {
    }

    private RefreshToken(Guid id, Guid tenantId, Guid userId, Guid familyId, string tokenHash, DateTime expiresAt, string? deviceInfo, string? ip)
        : base(id, tenantId)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        DeviceInfo = deviceInfo;
        IpAddress = ip;
    }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public string? ReplacedByTokenHash { get; private set; }

    public string? DeviceInfo { get; private set; }

    public string? IpAddress { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedAt is null && ExpiresAt > nowUtc;

    public static RefreshToken Issue(Guid tenantId, Guid userId, Guid? familyId, string tokenHash, DateTime nowUtc, TimeSpan lifetime, string? deviceInfo, string? ip) =>
        new(Guid.CreateVersion7(), tenantId, userId, familyId ?? Guid.CreateVersion7(), Guard.NotEmpty(tokenHash), nowUtc.Add(lifetime),
            deviceInfo is null ? null : Guard.MaxLength(deviceInfo, IdentityLimits.DeviceInfoMaxLength), ip);

    public void Rotate(string replacedByTokenHash, DateTime nowUtc)
    {
        RevokedAt = nowUtc;
        ReplacedByTokenHash = replacedByTokenHash;
    }

    public void Revoke(DateTime nowUtc) => RevokedAt ??= nowUtc;
}
