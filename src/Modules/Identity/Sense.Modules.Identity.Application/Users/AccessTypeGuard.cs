using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Users;

/// <summary>
/// Hesap sahibi koruması (11.3.01): Owner rolünü yalnız bir Owner verir/kaldırır. identity.users.manage izni (ör. İK
/// Yöneticisi, Muhasebe) tek başına bunu değiştirmeye yetmez. Tüm rol atama yollarında (kullanıcı grubu, rol listesi,
/// kullanıcı oluşturma) aynı kural uygulanır.
/// </summary>
public sealed class AccessTypeGuard(ICurrentUser currentUser, IUserRepository users, IRoleRepository roles)
{
    public async Task<Result> EnsureCanChangeAsync(IReadOnlyCollection<Guid> currentRoleIds, IReadOnlyCollection<Guid> newRoleIds, CancellationToken ct)
    {
        var ownerRole = await roles.GetByCodeAsync(SystemRoleCodes.Owner, ct).ConfigureAwait(false);
        var ownerChanges = ownerRole is not null && currentRoleIds.Contains(ownerRole.Id) != newRoleIds.Contains(ownerRole.Id);
        if (!ownerChanges)
        {
            return Result.Success();
        }

        var caller = currentUser.UserId is { } callerId ? await users.GetByIdAsync(callerId, ct).ConfigureAwait(false) : null;
        var callerIsOwner = caller is not null && caller.HasRole(ownerRole!.Id);
        return callerIsOwner ? Result.Success() : Error.Forbidden(IdentityErrors.AccessTypeOwnerOnly);
    }
}
