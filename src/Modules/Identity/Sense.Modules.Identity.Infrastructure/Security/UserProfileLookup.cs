using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Identity.Contracts.IUserProfileLookup: yalnız IdentityDbContext'e bağlı (Worker'da da çözülebilsin diye).</summary>
public sealed class UserProfileLookup(IdentityDbContext db) : IUserProfileLookup
{
    public async Task<IReadOnlyDictionary<Guid, UserProfileInfo>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserProfileInfo>();
        }

        var ids = userIds.Distinct().ToList();
        var rows = await db.Users.AsNoTracking().IgnoreAutoIncludes()
            .Where(u => u.TenantId == tenantId && ids.Contains(u.Id))
            .Select(u => new { u.Id, u.EmployeeId, u.Email, u.FirstName, u.LastName, u.PreferredLanguage, u.Status })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.ToDictionary(
            u => u.Id,
            u => new UserProfileInfo(
                u.Id,
                u.EmployeeId,
                u.Email,
                $"{u.FirstName} {u.LastName}".Trim(),
                string.IsNullOrWhiteSpace(u.PreferredLanguage) ? null : u.PreferredLanguage,
                u.Status == UserStatus.Active));
    }
}
