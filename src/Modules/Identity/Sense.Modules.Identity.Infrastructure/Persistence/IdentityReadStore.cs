using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Querying;

namespace Sense.Modules.Identity.Infrastructure.Persistence;

/// <summary>Okuma tarafı: grid standardına uygun (whitelist filtre/sıralama) EF projeksiyonları.</summary>
public sealed class IdentityReadStore(IdentityDbContext db) : IIdentityReadStore
{
    private static readonly GridFieldMap<User> UserFields = GridFieldMap<User>.Create()
        .Field(UserGridFields.Email, u => u.Email)
        .Field(UserGridFields.FirstName, u => u.FirstName)
        .Field(UserGridFields.LastName, u => u.LastName)
        .Field(UserGridFields.Status, u => u.Status)
        .Field(UserGridFields.EmployeeId, u => u.EmployeeId)
        .Field(UserGridFields.MfaEnabled, u => u.MfaEnabled)
        .Field(UserGridFields.LastLoginAt, u => u.LastLoginAt)
        .Field(UserGridFields.CreatedAt, u => u.CreatedAt);

    private static readonly GridFieldMap<Invitation> InvitationFields = GridFieldMap<Invitation>.Create()
        .Field(InvitationGridFields.Email, i => i.Email)
        .Field(InvitationGridFields.Status, i => i.Status)
        .Field(InvitationGridFields.ExpiresAt, i => i.ExpiresAt)
        .Field(InvitationGridFields.CreatedAt, i => i.CreatedAt);

    public async Task<PagedResult<UserListItemDto>> ListUsersAsync(PagedQuery paging, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(paging.Q))
        {
            var pattern = LikePattern.Contains(paging.Q);
            query = query.Where(u => EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.FirstName, pattern) || EF.Functions.ILike(u.LastName, pattern));
        }

        query = query.ApplyFilters(paging.Filters, UserFields).ApplySort(paging.SortClauses, UserFields, u => u.CreatedAt);

        var page = await query.ToPagedResultAsync(paging, ct).ConfigureAwait(false);
        var roleIds = page.Items.SelectMany(u => u.Roles.Select(r => r.RoleId)).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id))
            .Select(r => new RoleRefDto(r.Id, r.Code, r.Name)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);

        return page.Map(u => new UserListItemDto(u.Id, u.Email, u.FirstName, u.LastName, u.Phone, u.Status, u.EmployeeId, u.MfaEnabled, u.LastLoginAt,
            u.Roles.Select(r => roles.GetValueOrDefault(r.RoleId)).Where(r => r is not null).Select(r => r!).ToList(), u.CreatedAt));
    }

    public async Task<UserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct).ConfigureAwait(false);
        if (u is null)
        {
            return null;
        }

        var roleIds = u.Roles.Select(r => r.RoleId).ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).Select(r => new RoleRefDto(r.Id, r.Code, r.Name)).ToListAsync(ct).ConfigureAwait(false);

        return new UserDetailDto(u.Id, u.Email, u.EmailConfirmed, u.FirstName, u.LastName, u.Phone, u.Status, u.EmployeeId, u.MfaEnabled, u.LastLoginAt,
            u.LockoutEndUtc, u.PreferredLanguage, roles, u.PermissionOverrides.Select(p => new PermissionOverrideDto(p.Permission, p.Allow)).ToList(),
            u.CreatedAt, u.ModifiedDate);
    }

    public async Task<PagedResult<InvitationDto>> ListInvitationsAsync(PagedQuery paging, CancellationToken ct)
    {
        var query = db.Invitations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(paging.Q))
        {
            query = query.Where(i => EF.Functions.ILike(i.Email, LikePattern.Contains(paging.Q)));
        }

        query = query.ApplyFilters(paging.Filters, InvitationFields).ApplySort(paging.SortClauses, InvitationFields, i => i.CreatedAt);
        var page = await query.ToPagedResultAsync(paging, ct).ConfigureAwait(false);
        return page.Map(i => new InvitationDto(i.Id, i.UserId, i.Email, i.Status.ToString(), i.ExpiresAt, i.AcceptedAt, i.SendCount, i.CreatedAt));
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        var counts = await db.Users.AsNoTracking().SelectMany(u => u.Roles).GroupBy(r => r.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.RoleId, x => x.Count, ct).ConfigureAwait(false);
        return roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RoleDto?> GetRoleAsync(Guid roleId, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct).ConfigureAwait(false);
        if (role is null)
        {
            return null;
        }

        var count = await db.Users.AsNoTracking().CountAsync(u => u.Roles.Any(r => r.RoleId == roleId), ct).ConfigureAwait(false);
        return ToDto(role, count);
    }

    private static RoleDto ToDto(Role r, int userCount) => new(r.Id, r.Code, r.Name, r.Description, r.IsSystem, r.IsLocked, userCount,
        r.Permissions.Select(p => new RolePermissionDto(p.Permission, (DataScope)p.Scope, RolePermissionMapper.ParseScopeIds(p.ScopeIdsJson))).ToList());
}

public static class LikePattern
{
    private const string Wildcard = "%";

    public static string Contains(string term) => string.Concat(Wildcard, term.Trim(), Wildcard);
}

public static class UserGridFields
{
    public const string Email = "email";
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string Status = "status";
    public const string EmployeeId = "employeeId";
    public const string MfaEnabled = "mfaEnabled";
    public const string LastLoginAt = "lastLoginAt";
    public const string CreatedAt = "createdAt";
}

public static class InvitationGridFields
{
    public const string Email = "email";
    public const string Status = "status";
    public const string ExpiresAt = "expiresAt";
    public const string CreatedAt = "createdAt";
}
