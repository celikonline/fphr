using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Impersonation;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Tokens;
using Sense.Modules.Identity.Domain.UnitAuthorizations;
using Sense.Modules.Identity.Domain.Users;

namespace Sense.Modules.Identity.Infrastructure.Persistence;

public sealed class TenantRepository(IdentityDbContext db) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct) => db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct) => db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) => db.Tenants.AnyAsync(t => t.Slug == slug, ct);

    public void Add(Tenant tenant) => db.Tenants.Add(tenant);
}

public sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
    }

    public Task<User?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, ct);

    /// <summary>Giriş ekranı: kiracı filtresi bilinçli olarak kaldırılır (yalnız e-posta eşleşmesi, silinmişler hariç).</summary>
    public async Task<IReadOnlyList<(User User, Guid TenantId)>> FindAcrossTenantsByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var users = await db.Users
            .IgnoreQueryFilters([IdentityDbContext.TenantFilter])
            .Where(u => u.NormalizedEmail == normalized)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return users.Select(u => (u, u.TenantId)).ToList();
    }

    public Task<bool> EmailExistsAsync(string email, Guid? excludeUserId, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return db.Users.AnyAsync(u => u.NormalizedEmail == normalized && (excludeUserId == null || u.Id != excludeUserId), ct);
    }

    public Task<int> CountByRoleAsync(Guid roleId, CancellationToken ct) =>
        db.Users.CountAsync(u => u.Roles.Any(r => r.RoleId == roleId), ct);

    public async Task<int> CountActiveOwnersAsync(CancellationToken ct)
    {
        var ownerId = await db.Roles.Where(r => r.Code == SystemRoleCodes.Owner).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (ownerId is null)
        {
            return 0;
        }

        return await db.Users.CountAsync(u => u.Status == UserStatus.Active && u.Roles.Any(r => r.RoleId == ownerId), ct).ConfigureAwait(false);
    }

    public void Add(User user) => db.Users.Add(user);
}

public sealed class RoleRepository(IdentityDbContext db) : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct) => db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Role?> GetByCodeAsync(string code, CancellationToken ct) => db.Roles.FirstOrDefaultAsync(r => r.Code == code, ct);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? [] : await db.Roles.Where(r => list.Contains(r.Id)).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct) => await db.Roles.OrderBy(r => r.Code).ToListAsync(ct).ConfigureAwait(false);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct) => db.Roles.AnyAsync(r => r.Code == code, ct);

    public void Add(Role role) => db.Roles.Add(role);

    public void Remove(Role role) => db.Roles.Remove(role);
}

public sealed class InvitationRepository(IdentityDbContext db) : IInvitationRepository
{
    public Task<Invitation?> GetByIdAsync(Guid id, CancellationToken ct) => db.Invitations.FirstOrDefaultAsync(i => i.Id == id, ct);

    /// <summary>Anonim kabul akışı: token eşleşmesi kiracı bilinmeden yapılır.</summary>
    public Task<Invitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.Invitations.IgnoreQueryFilters([IdentityDbContext.TenantFilter]).FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

    public Task<Invitation?> GetPendingByUserAsync(Guid userId, CancellationToken ct) =>
        db.Invitations.Where(i => i.UserId == userId && i.Status == InvitationStatus.Pending).OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);

    public void Add(Invitation invitation) => db.Invitations.Add(invitation);
}

public sealed class RefreshTokenRepository(IdentityDbContext db) : IRefreshTokenRepository
{
    /// <summary>Refresh akışında kiracı henüz bilinmez: filtre kaldırılır, kiracı token kaydından okunur.</summary>
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.IgnoreQueryFilters([IdentityDbContext.TenantFilter]).FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken ct) =>
        await db.RefreshTokens.Where(t => t.FamilyId == familyId).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct) =>
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > nowUtc).ToListAsync(ct).ConfigureAwait(false);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
}

public sealed class PasswordResetTokenRepository(IdentityDbContext db) : IPasswordResetTokenRepository
{
    /// <summary>Anonim sıfırlama akışında kiracı henüz bilinmez: filtre kaldırılır, kiracı token kaydından okunur.</summary>
    public Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.PasswordResetTokens.IgnoreQueryFilters([IdentityDbContext.TenantFilter]).FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public void Add(PasswordResetToken token) => db.PasswordResetTokens.Add(token);
}

public sealed class ImpersonationGrantRepository(IdentityDbContext db) : IImpersonationGrantRepository
{
    public Task<ImpersonationGrant?> GetByIdAsync(Guid id, CancellationToken ct) => db.ImpersonationGrants.FirstOrDefaultAsync(g => g.Id == id, ct);

    public void Add(ImpersonationGrant grant) => db.ImpersonationGrants.Add(grant);

    public async Task AddAndSaveAsync(ImpersonationGrant grant, CancellationToken ct)
    {
        db.ImpersonationGrants.Add(grant);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed class UnitAuthorizationRepository(IdentityDbContext db) : IUnitAuthorizationRepository
{
    public Task<UnitAuthorization?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.UnitAuthorizations.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<UnitAuthorization?> GetActiveAsync(Guid orgUnitId, Guid userId, CancellationToken ct) =>
        db.UnitAuthorizations.FirstOrDefaultAsync(a => a.OrgUnitId == orgUnitId && a.UserId == userId && a.IsActive, ct);

    public async Task<IReadOnlyList<UnitAuthorization>> ListActiveByOrgUnitAsync(Guid orgUnitId, CancellationToken ct) =>
        await db.UnitAuthorizations.Where(a => a.OrgUnitId == orgUnitId && a.IsActive)
            .OrderBy(a => a.GrantedAt).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<UnitAuthorization>> ListActiveByUserAsync(Guid userId, CancellationToken ct) =>
        await db.UnitAuthorizations.Where(a => a.UserId == userId && a.IsActive).ToListAsync(ct).ConfigureAwait(false);

    public void Add(UnitAuthorization grant) => db.UnitAuthorizations.Add(grant);
}
