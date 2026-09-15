using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Users;

/// <summary>
/// Erişim türü yükseltme koruması (11.3.01): Owner rolünü yalnız bir Owner verir/kaldırır; Admin rolünü yalnız Owner veya Admin
/// verir/kaldırır. identity.users.manage izni (ör. HR) tek başına bu iki rolü değiştirmeye yetmez. Tüm rol atama yollarında
/// (erişim türü, rol listesi, kullanıcı oluşturma) aynı kural uygulanır.
/// </summary>
public sealed class AccessTypeGuard(ICurrentUser currentUser, IUserRepository users, IRoleRepository roles)
{
    public async Task<Result> EnsureCanChangeAsync(IReadOnlyCollection<Guid> currentRoleIds, IReadOnlyCollection<Guid> newRoleIds, CancellationToken ct)
    {
        var ownerRole = await roles.GetByCodeAsync(SystemRoleCodes.Owner, ct).ConfigureAwait(false);
        var adminRole = await roles.GetByCodeAsync(SystemRoleCodes.Admin, ct).ConfigureAwait(false);

        var ownerChanges = ownerRole is not null && currentRoleIds.Contains(ownerRole.Id) != newRoleIds.Contains(ownerRole.Id);
        var adminChanges = adminRole is not null && currentRoleIds.Contains(adminRole.Id) != newRoleIds.Contains(adminRole.Id);
        if (!ownerChanges && !adminChanges)
        {
            return Result.Success();
        }

        var caller = currentUser.UserId is { } callerId ? await users.GetByIdAsync(callerId, ct).ConfigureAwait(false) : null;
        var callerIsOwner = caller is not null && ownerRole is not null && caller.HasRole(ownerRole.Id);
        var callerIsAdmin = caller is not null && adminRole is not null && caller.HasRole(adminRole.Id);

        if (ownerChanges && !callerIsOwner)
        {
            return Error.Forbidden(IdentityErrors.AccessTypeOwnerOnly);
        }

        if (adminChanges && !callerIsOwner && !callerIsAdmin)
        {
            return Error.Forbidden(IdentityErrors.AccessTypeForbidden);
        }

        return Result.Success();
    }
}
