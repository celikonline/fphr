using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Identity.Contracts.IUserAccountLookup: kullanıcı durumu + erişim tipi (sistem rolü) + son davet zamanı.</summary>
public sealed class UserAccountLookup(IdentityDbContext db) : IUserAccountLookup
{
    private static readonly string[] AccessTypePrecedence = [SystemRoleCodes.Owner, SystemRoleCodes.Admin, SystemRoleCodes.Employee];

    public async Task<IReadOnlyDictionary<Guid, UserAccountInfo>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserAccountInfo>();
        }

        var ids = userIds.Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && ids.Contains(u.Id))
            .ToListAsync(ct).ConfigureAwait(false);

        var roleIds = users.SelectMany(u => u.Roles.Select(r => r.RoleId)).Distinct().ToList();
        var accessRoles = await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId && roleIds.Contains(r.Id) && r.IsSystem && AccessTypePrecedence.Contains(r.Code))
            .Select(r => new { r.Id, r.Code })
            .ToDictionaryAsync(r => r.Id, r => r.Code, ct).ConfigureAwait(false);

        var invitations = await db.Invitations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && ids.Contains(i.UserId))
            .Select(i => new { i.UserId, i.Status, i.CreatedAt, i.ModifiedDate })
            .ToListAsync(ct).ConfigureAwait(false);

        var lastSent = invitations
            .GroupBy(i => i.UserId)
            .ToDictionary(
                g => g.Key,
                // Kabul, ModifiedDate'i günceller; kabul edilmiş davette gönderim anı olarak oluşturma zamanı kullanılır.
                g => g.Max(i => i.Status == InvitationStatus.Accepted ? i.CreatedAt : i.ModifiedDate ?? i.CreatedAt));

        var result = new Dictionary<Guid, UserAccountInfo>(users.Count);
        foreach (var user in users)
        {
            var codes = user.Roles.Select(r => accessRoles.GetValueOrDefault(r.RoleId)).Where(c => c is not null).ToHashSet(StringComparer.Ordinal);
            var accessType = AccessTypePrecedence.FirstOrDefault(codes.Contains);
            result[user.Id] = new UserAccountInfo(
                user.Id,
                MapStatus(user.Status),
                accessType,
                lastSent.TryGetValue(user.Id, out var sentAt) ? sentAt : null);
        }

        return result;
    }

    private static UserAccountStatus MapStatus(UserStatus status) => status switch
    {
        UserStatus.Invited => UserAccountStatus.Invited,
        UserStatus.Active => UserAccountStatus.Active,
        UserStatus.Locked => UserAccountStatus.Locked,
        UserStatus.Disabled => UserAccountStatus.Disabled,
        _ => UserAccountStatus.None,
    };
}
