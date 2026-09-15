using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Identity.Domain.Invitations;

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Expired = 2,
    Revoked = 3,
}

/// <summary>Davetin gönderildiği adresin kaynağı: çalışanın iş/kişisel e-postası ya da kullanıcı hesabının kendi e-postası.</summary>
public enum InvitationEmailSource
{
    Account = 0,
    Work = 1,
    Personal = 2,
}

/// <summary>Kullanıcı daveti. Token yalnız hash olarak saklanır; ham token e-posta bağlantısında taşınır.</summary>
public sealed class Invitation : TenantAggregateRoot<Guid>
{
    private Invitation()
    {
    }

    private Invitation(
        Guid id, Guid tenantId, Guid userId, string email, string tokenHash, DateTime expiresAt, Guid? invitedByUserId, InvitationEmailSource emailSource, Guid? batchId)
        : base(id, tenantId)
    {
        EmailSource = emailSource;
        BatchId = batchId;
        UserId = userId;
        Email = email;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        InvitedByUserId = invitedByUserId;
        Status = InvitationStatus.Pending;
        SendCount = 1;
    }

    public Guid UserId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }

    public InvitationStatus Status { get; private set; }

    public Guid? InvitedByUserId { get; private set; }

    public DateTime? AcceptedAt { get; private set; }

    public int SendCount { get; private set; }

    /// <summary><see cref="Email"/> = son gönderilen adres; bu alan onun kaynağıdır.</summary>
    public InvitationEmailSource EmailSource { get; private set; }

    /// <summary>Toplu "Giriş bilgilerini gönder" partisi (tekil gönderimde null).</summary>
    public Guid? BatchId { get; private set; }

    public static Invitation Create(
        Guid tenantId,
        Guid userId,
        string email,
        string tokenHash,
        DateTime nowUtc,
        TimeSpan validity,
        Guid? invitedByUserId,
        InvitationEmailSource emailSource = InvitationEmailSource.Account,
        Guid? batchId = null)
    {
        var inv = new Invitation(
            Guid.CreateVersion7(), tenantId, userId, EmailAddress.Of(email).Value, Guard.NotEmpty(tokenHash), nowUtc.Add(validity), invitedByUserId, emailSource, batchId);
        inv.Raise(new InvitationCreated(inv.Id, tenantId, userId, inv.Email));
        return inv;
    }

    public bool IsUsable(DateTime nowUtc) => Status == InvitationStatus.Pending && ExpiresAt > nowUtc;

    public Result Accept(DateTime nowUtc)
    {
        if (Status == InvitationStatus.Accepted)
        {
            return Error.Conflict(IdentityDomainErrors.InvitationAlreadyAccepted);
        }

        if (!IsUsable(nowUtc))
        {
            return Error.Rule(IdentityDomainErrors.InvitationExpired);
        }

        Status = InvitationStatus.Accepted;
        AcceptedAt = nowUtc;
        Raise(new InvitationAccepted(Id, TenantId, UserId));
        return Result.Success();
    }

    public void Revoke()
    {
        if (Status == InvitationStatus.Pending)
        {
            Status = InvitationStatus.Revoked;
        }
    }

    /// <summary>Yeniden gönderim: yeni token, süre uzatılır; hedef adres verilirse (çalışan e-postası değiştiyse) güncellenir.</summary>
    public Result Renew(string newTokenHash, DateTime nowUtc, TimeSpan validity, string? email = null, InvitationEmailSource? emailSource = null, Guid? batchId = null)
    {
        if (Status == InvitationStatus.Accepted)
        {
            return Error.Conflict(IdentityDomainErrors.InvitationAlreadyAccepted);
        }

        if (email is not null)
        {
            Email = EmailAddress.Of(email).Value;
        }

        EmailSource = emailSource ?? EmailSource;
        BatchId = batchId;
        TokenHash = Guard.NotEmpty(newTokenHash);
        ExpiresAt = nowUtc.Add(validity);
        Status = InvitationStatus.Pending;
        SendCount++;
        Raise(new InvitationCreated(Id, TenantId, UserId, Email));
        return Result.Success();
    }
}

public sealed record InvitationCreated(Guid InvitationId, Guid TenantId, Guid UserId, string Email) : DomainEvent;

public sealed record InvitationAccepted(Guid InvitationId, Guid TenantId, Guid UserId) : DomainEvent;
