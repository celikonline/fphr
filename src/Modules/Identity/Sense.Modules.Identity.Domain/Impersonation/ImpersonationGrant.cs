using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Domain.Impersonation;

public enum ImpersonationGrantStatus
{
    Pending = 0,
    Approved = 1,
    Active = 2,
    Expired = 3,
    Revoked = 4,
}

/// <summary>
/// Platform personelinin kiracı verisine geçici, onaylı ve süreli erişim talebi.
/// TenantId = erişilecek (impersonate edilecek) kiracı; PlatformUserId = talep eden platform kullanıcısının
/// kendi (platform) kiracısındaki User.Id'si (bu kiracıda ayrı bir User kaydı yoktur, yalnız referans).
/// Akış: Request (Pending) → Approve (tenant Owner/Admin, zaman penceresi açılır) → Start (bir kez kullanılır,
/// token üretimi tetiklenir) → süre dolunca bir sonraki dokunuşta Expired'a döner. Her geçiş bir domain event
/// raise eder; AuditTenantInterceptor bunları outbox'a yazar (audit izi).
/// </summary>
public sealed class ImpersonationGrant : TenantAggregateRoot<Guid>, IAuditLogged
{
    private ImpersonationGrant()
    {
    }

    private ImpersonationGrant(Guid id, Guid tenantId, Guid platformUserId, string reason, DateTime requestedAt) : base(id, tenantId)
    {
        PlatformUserId = platformUserId;
        Reason = reason;
        RequestedAt = requestedAt;
        Status = ImpersonationGrantStatus.Pending;
    }

    /// <summary>İstekte bulunan platform kullanıcısı.</summary>
    public Guid PlatformUserId { get; private set; }

    /// <summary>Onaylayan kiracı tarafı kullanıcı (Owner/Admin).</summary>
    public Guid? ApprovedByUserId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTime? ApprovedAt { get; private set; }

    public DateTime RequestedAt { get; private set; }

    /// <summary>Onay ile birlikte belirlenen kullanım penceresi bitişi.</summary>
    public DateTime? ExpiresAt { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public ImpersonationGrantStatus Status { get; private set; }

    /// <summary>Oturum hâlâ kullanılabilir mi: başlatılmış, iptal edilmemiş ve süresi dolmamış (durum geçişi tembel olduğundan ExpiresAt ayrıca kontrol edilir).</summary>
    public bool IsSessionActive(DateTime nowUtc) => Status == ImpersonationGrantStatus.Active && ExpiresAt is { } exp && exp > nowUtc;

    /// <summary>Görüntülenen durum: süresi dolmuş ama henüz dokunulmamış Approved/Active grant Expired görünür.</summary>
    public ImpersonationGrantStatus EffectiveStatus(DateTime nowUtc) =>
        Status is ImpersonationGrantStatus.Approved or ImpersonationGrantStatus.Active && ExpiresAt is { } exp && exp <= nowUtc
            ? ImpersonationGrantStatus.Expired
            : Status;

    public static Result<ImpersonationGrant> Create(Guid tenantId, Guid platformUserId, string reason, DateTime nowUtc)
    {
        var trimmed = Guard.MaxLength(Guard.NotEmpty(reason), IdentityLimits.ReasonMaxLength);
        var grant = new ImpersonationGrant(Guid.CreateVersion7(), tenantId, platformUserId, trimmed, nowUtc);
        grant.Raise(new ImpersonationRequested(grant.Id, tenantId, platformUserId, trimmed, nowUtc));
        return grant;
    }

    /// <summary>Tenant Owner/Admin onayı: kullanım penceresi (ExpiresAt) açılır.</summary>
    public Result Approve(Guid approvedByUserId, DateTime nowUtc, TimeSpan window)
    {
        if (Status != ImpersonationGrantStatus.Pending)
        {
            return Error.Rule(IdentityDomainErrors.ImpersonationNotPending);
        }

        ApprovedByUserId = approvedByUserId;
        ApprovedAt = nowUtc;
        ExpiresAt = nowUtc.Add(window);
        Status = ImpersonationGrantStatus.Approved;
        Raise(new ImpersonationApproved(Id, TenantId, PlatformUserId, approvedByUserId, ExpiresAt.Value));
        return Result.Success();
    }

    /// <summary>Bir kez kullanılır: token üretimi bu adımdan sonra yapılır. Süresi geçmişse Expired'a düşer.</summary>
    public Result Start(DateTime nowUtc)
    {
        if (Status == ImpersonationGrantStatus.Approved && ExpiresAt is { } exp && exp <= nowUtc)
        {
            Status = ImpersonationGrantStatus.Expired;
            Raise(new ImpersonationExpired(Id, TenantId, PlatformUserId));
            return Error.Rule(IdentityDomainErrors.ImpersonationExpired);
        }

        if (Status != ImpersonationGrantStatus.Approved)
        {
            return Error.Rule(IdentityDomainErrors.ImpersonationNotApproved);
        }

        UsedAt = nowUtc;
        Status = ImpersonationGrantStatus.Active;
        Raise(new ImpersonationStarted(Id, TenantId, PlatformUserId, ExpiresAt!.Value));
        return Result.Success();
    }

    /// <summary>Onay bekleyen veya onaylanmış (henüz/hâlâ kullanılan) bir grantı iptal eder.</summary>
    public Result Revoke(DateTime nowUtc)
    {
        if (Status is ImpersonationGrantStatus.Revoked or ImpersonationGrantStatus.Expired)
        {
            return Error.Rule(IdentityDomainErrors.ImpersonationNotRevocable);
        }

        RevokedAt = nowUtc;
        Status = ImpersonationGrantStatus.Revoked;
        Raise(new ImpersonationRevoked(Id, TenantId, PlatformUserId));
        return Result.Success();
    }
}

public sealed record ImpersonationRequested(Guid GrantId, Guid TenantId, Guid PlatformUserId, string Reason, DateTime RequestedAt) : DomainEvent;

public sealed record ImpersonationApproved(Guid GrantId, Guid TenantId, Guid PlatformUserId, Guid ApprovedByUserId, DateTime ExpiresAt) : DomainEvent;

public sealed record ImpersonationStarted(Guid GrantId, Guid TenantId, Guid PlatformUserId, DateTime ExpiresAt) : DomainEvent;

public sealed record ImpersonationExpired(Guid GrantId, Guid TenantId, Guid PlatformUserId) : DomainEvent;

public sealed record ImpersonationRevoked(Guid GrantId, Guid TenantId, Guid PlatformUserId) : DomainEvent;
