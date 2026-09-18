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

    /// <summary>Fesih olayı (G-3-01) gibi çapraz modül tüketicileri için: çalışan kaydına bağlı kullanıcı (varsa).</summary>
    Task<User?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct);

    /// <summary>Kiracı filtresi olmadan: giriş ekranında e-posta → kiracı listesi.</summary>
    Task<IReadOnlyList<(User User, Guid TenantId)>> FindAcrossTenantsByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Kiracı filtresi olmadan, id ile: impersonation oturumunda JWT'nin `sub`'ı platform kullanıcısının
    /// PLATFORM kiracısındaki satırıdır, hedef (impersonate edilen) kiracının filtresiyle bulunamaz
    /// (GetMeHandler'ın bu yüzden 401 döndüğü, faz0-durum-takibi.md #11'de belgelenen boşluk).
    /// </summary>
    Task<User?> GetByIdAcrossTenantsAsync(Guid id, CancellationToken ct);

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

public interface IPasswordResetTokenRepository
{
    /// <summary>Anonim sıfırlama akışı: kiracı filtresi olmadan token hash'i ile.</summary>
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);

    void Add(PasswordResetToken token);
}

public interface IImpersonationGrantRepository
{
    Task<ImpersonationGrant?> GetByIdAsync(Guid id, CancellationToken ct);

    void Add(ImpersonationGrant grant);

    /// <summary>
    /// Ekler ve hemen kaydeder: talep, çağıranın (platform) kiracısından hedef kiracının kapsamına geçilerek oluşturulur; komutun
    /// unit of work'ü kapsam sona erdikten sonra (platform kiracısında) kaydedeceği için kiracı yazma denetimine takılırdı.
    /// </summary>
    Task AddAndSaveAsync(ImpersonationGrant grant, CancellationToken ct);
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
