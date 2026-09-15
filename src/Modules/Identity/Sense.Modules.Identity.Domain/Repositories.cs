using Sense.Modules.Identity.Domain.Impersonation;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Tokens;
using Sense.Modules.Identity.Domain.UnitAuthorizations;
using Sense.Modules.Identity.Domain.Users;

namespace Sense.Modules.Identity.Domain;

/// <summary>Repository portları (Onion: Domain tanımlar, Infrastructure implement eder).</summary>
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    void Add(Tenant tenant);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<User?> GetByEmailAsync(string email, CancellationToken ct);

    /// <summary>Kiracı filtresi olmadan: giriş ekranında e-posta → kiracı listesi.</summary>
    Task<IReadOnlyList<(User User, Guid TenantId)>> FindAcrossTenantsByEmailAsync(string email, CancellationToken ct);

    Task<bool> EmailExistsAsync(string email, Guid? excludeUserId, CancellationToken ct);

    Task<int> CountByRoleAsync(Guid roleId, CancellationToken ct);

    Task<int> CountActiveOwnersAsync(CancellationToken ct);

    void Add(User user);
}

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Role?> GetByCodeAsync(string code, CancellationToken ct);

    Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct);

    Task<bool> CodeExistsAsync(string code, CancellationToken ct);

    void Add(Role role);

    void Remove(Role role);
}

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Anonim kabul akışı: kiracı filtresi olmadan token hash'i ile.</summary>
    Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);

    Task<Invitation?> GetPendingByUserAsync(Guid userId, CancellationToken ct);

    void Add(Invitation invitation);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken ct);

    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct);

    void Add(RefreshToken token);
}

public interface IImpersonationGrantRepository
{
    Task<ImpersonationGrant?> GetByIdAsync(Guid id, CancellationToken ct);

    void Add(ImpersonationGrant grant);
}

public interface IUnitAuthorizationRepository
{
    Task<UnitAuthorization?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>(OrgUnit, User) çifti için mevcut aktif grant (varsa) - Grant komutunun upsert semantiği için.</summary>
    Task<UnitAuthorization?> GetActiveAsync(Guid orgUnitId, Guid userId, CancellationToken ct);

    /// <summary>Bir birimin üç nokta menüsü "Birim yetkilendirme" listesi.</summary>
    Task<IReadOnlyList<UnitAuthorization>> ListActiveByOrgUnitAsync(Guid orgUnitId, CancellationToken ct);

    /// <summary>Bir kullanıcının aktif olduğu tüm birimler - ileride IDataScopeService/IEmployeeScopeLookup tarzı bir tüketici için (bkz. UnitAuthorization doc).</summary>
    Task<IReadOnlyList<UnitAuthorization>> ListActiveByUserAsync(Guid userId, CancellationToken ct);

    void Add(UnitAuthorization grant);
}
