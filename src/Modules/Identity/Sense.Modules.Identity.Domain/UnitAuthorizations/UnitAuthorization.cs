using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Domain.UnitAuthorizations;

/// <summary>
/// Birim yetkilendirme (Faz 0 madde 3, roadmap 11.3.1/11.3.2): departman ağacında bir OrgUnit için bir
/// kullanıcıya verilen ek grant - "Çalışanların ödeme ve maaş bilgilerine erişebilsin"
/// (<see cref="CanViewSalaryAndPayments"/>) ve/veya "Kendi biriminin raporlarına erişebilsin"
/// (<see cref="CanViewUnitReports"/>). <see cref="OrgUnitId"/> Organization modülüne salt bir Guid
/// referanstır (EF navigation/DB FK yok, ADR 0001 modül sınırları); varlığı Application katmanında
/// Sense.Modules.Organization.Contracts.IOrgUnitLookup ile doğrulanır (bkz. GrantUnitAuthorizationHandler).
///
/// Aynı kullanıcı birden çok OrgUnit için yetkilendirilebilir ("aynı kişi birden fazla birime"); aynı
/// (OrgUnitId, UserId) çifti için tek aktif kayıt olur - <see cref="Revoke"/> soft-revoke yapar
/// (IsActive=false, geçmiş korunur; bkz. IdentityDbContext'teki (TenantId, OrgUnitId, UserId) partial
/// unique index), yeniden yetkilendirme (aynı birim/kişi) mevcut aktif kaydı <see cref="UpdateGrants"/>
/// ile günceller (upsert - bkz. GrantUnitAuthorizationHandler).
///
/// DataScope bağlantısı (Faz 0 madde 3/5): CanViewUnitReports=true iken bu, kullanıcının bu belirli
/// OrgUnit'in verisi için DataScope.Department (OrgUnit + alt birimleri) kapsamına sahip olmasıyla
/// eşdeğerdir; CanViewSalaryAndPayments ek olarak maaş/ödeme alanlarını bu kapsama dahil eder. Bu görev
/// yalnız grant/revoke/list mekanizmasını kurar - henüz hiçbir modülün sorgusu bu kapsamı gerçekten
/// uygulamıyor (roadmap'in kendi notu: "test edecek başka modül sorgusu yok"). İleride bir
/// IEmployeeScopeLookup tarzı tüketici (bkz. Sense.Shared.Contracts.Security.IEmployeeScopeLookup)
/// "bu kullanıcı hangi OrgUnit'ler için Department kapsamında görünür" sorusunu
/// IUnitAuthorizationRepository.ListActiveByUserAsync ile yanıtlayabilir; bu tip o zaman için bilhassa
/// bu şekilde (OrgUnitId + iki bool grant başına bir satır) tasarlanmıştır.
/// </summary>
public sealed class UnitAuthorization : TenantAggregateRoot<Guid>, IAuditLogged
{
    private UnitAuthorization()
    {
    }

    private UnitAuthorization(
        Guid id,
        Guid tenantId,
        Guid orgUnitId,
        Guid userId,
        bool canViewSalaryAndPayments,
        bool canViewUnitReports,
        Guid grantedByUserId,
        DateTime grantedAt)
        : base(id, tenantId)
    {
        OrgUnitId = orgUnitId;
        UserId = userId;
        CanViewSalaryAndPayments = canViewSalaryAndPayments;
        CanViewUnitReports = canViewUnitReports;
        GrantedByUserId = grantedByUserId;
        GrantedAt = grantedAt;
        IsActive = true;
    }

    /// <summary>Cross-module reference to Organization.OrgUnit; validated at the Application layer via IOrgUnitLookup.</summary>
    public Guid OrgUnitId { get; private set; }

    /// <summary>Cross-module reference to Identity.User (yetkilendirilen kişi).</summary>
    public Guid UserId { get; private set; }

    /// <summary>"Çalışanların ödeme ve maaş bilgilerine erişebilsin".</summary>
    public bool CanViewSalaryAndPayments { get; private set; }

    /// <summary>"Kendi biriminin raporlarına erişebilsin" ("Kendin oluştur" hariç - bu grant setinde hiçbir zaman verilmez).</summary>
    public bool CanViewUnitReports { get; private set; }

    public Guid GrantedByUserId { get; private set; }

    public DateTime GrantedAt { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public static Result<UnitAuthorization> Grant(
        Guid tenantId,
        Guid orgUnitId,
        Guid userId,
        bool canViewSalaryAndPayments,
        bool canViewUnitReports,
        Guid grantedByUserId,
        DateTime nowUtc)
    {
        Guard.NotDefault(orgUnitId);
        Guard.NotDefault(userId);

        if (!canViewSalaryAndPayments && !canViewUnitReports)
        {
            return Error.Validation(IdentityDomainErrors.UnitAuthorizationNoGrantsSelected);
        }

        var grant = new UnitAuthorization(
            Guid.CreateVersion7(), tenantId, orgUnitId, userId, canViewSalaryAndPayments, canViewUnitReports, grantedByUserId, nowUtc);
        grant.Raise(new UnitAuthorizationGranted(grant.Id, tenantId, orgUnitId, userId, canViewSalaryAndPayments, canViewUnitReports, grantedByUserId, nowUtc));
        return grant;
    }

    /// <summary>
    /// Birim ağacı satır menüsündeki "Birim yetkilendirme" diyaloğunun tekrar "Kaydet"lenmesi: aynı
    /// (OrgUnit, User) için zaten aktif bir grant varsa yeni satır oluşturmak yerine anahtarlar
    /// güncellenir (upsert semantics - bkz. GrantUnitAuthorizationHandler).
    /// </summary>
    public Result UpdateGrants(bool canViewSalaryAndPayments, bool canViewUnitReports, Guid grantedByUserId, DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Error.Rule(IdentityDomainErrors.UnitAuthorizationAlreadyRevoked);
        }

        if (!canViewSalaryAndPayments && !canViewUnitReports)
        {
            return Error.Validation(IdentityDomainErrors.UnitAuthorizationNoGrantsSelected);
        }

        CanViewSalaryAndPayments = canViewSalaryAndPayments;
        CanViewUnitReports = canViewUnitReports;
        GrantedByUserId = grantedByUserId;
        GrantedAt = nowUtc;
        Raise(new UnitAuthorizationGranted(Id, TenantId, OrgUnitId, UserId, canViewSalaryAndPayments, canViewUnitReports, grantedByUserId, nowUtc));
        return Result.Success();
    }

    /// <summary>Soft-revoke: IsActive=false, geçmiş kayıt korunur. Aynı (OrgUnit, User) için yeniden yetkilendirme yeni bir Grant ile yapılır.</summary>
    public Result Revoke(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Error.Rule(IdentityDomainErrors.UnitAuthorizationAlreadyRevoked);
        }

        IsActive = false;
        RevokedAt = nowUtc;
        Raise(new UnitAuthorizationRevoked(Id, TenantId, OrgUnitId, UserId));
        return Result.Success();
    }
}

public sealed record UnitAuthorizationGranted(
    Guid UnitAuthorizationId,
    Guid TenantId,
    Guid OrgUnitId,
    Guid UserId,
    bool CanViewSalaryAndPayments,
    bool CanViewUnitReports,
    Guid GrantedByUserId,
    DateTime GrantedAt) : DomainEvent;

public sealed record UnitAuthorizationRevoked(Guid UnitAuthorizationId, Guid TenantId, Guid OrgUnitId, Guid UserId) : DomainEvent;
