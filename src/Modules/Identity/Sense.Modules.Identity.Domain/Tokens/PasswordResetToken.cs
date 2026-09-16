using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Identity.Domain.Tokens;

/// <summary>
/// Parola sıfırlama token'ı. Davet (<see cref="Invitations.Invitation"/>) ile aynı kalıp: token yalnız hash olarak
/// saklanır, ham token e-posta bağlantısında taşınır, tek kullanımlıktır. Kullanıcı numaralandırmasına izin
/// vermemek için var olmayan/pasif hesaplarda da aynı akış (token üretmeden) izlenir; bkz. ForgotPasswordHandler.
/// </summary>
public sealed class PasswordResetToken : TenantEntity<Guid>
{
    private PasswordResetToken()
    {
    }

    private PasswordResetToken(Guid id, Guid tenantId, Guid userId, string tokenHash, DateTime expiresAt)
        : base(id, tenantId)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public static PasswordResetToken Issue(Guid tenantId, Guid userId, string tokenHash, DateTime nowUtc, TimeSpan validity) =>
        new(Guid.CreateVersion7(), tenantId, userId, Guard.NotEmpty(tokenHash), nowUtc.Add(validity));

    public bool IsUsable(DateTime nowUtc) => UsedAt is null && ExpiresAt > nowUtc;

    /// <summary>Tek kullanımlık: başarıyla tüketildiğinde işaretlenir, tekrar kullanılamaz.</summary>
    public void MarkUsed(DateTime nowUtc) => UsedAt = nowUtc;
}
