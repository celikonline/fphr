using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>Süper admin listesi satırı. <paramref name="IsPlatform"/>: platform kiracısının kendisi (müşteri değil).</summary>
public sealed record TenantSummaryDto(Guid Id, string Slug, string Name, TenantStatus Status, DateTime? TrialEndsAt, DateTime CreatedAt, bool IsPlatform);

/// <summary>Tüm şirketleri (kiracıları) listeler. Yalnız süper admin: izin + <c>User.IsPlatformStaff</c> (bkz. <see cref="CreateTenantCommand"/>).</summary>
[RequiresPermission(IdentityPermissions.PlatformTenantsManage)]
public sealed record ListTenantsQuery : IQuery<IReadOnlyList<TenantSummaryDto>>;

public sealed class ListTenantsHandler(ITenantRepository tenants, IUserRepository users, ICurrentUser currentUser)
    : IQueryHandler<ListTenantsQuery, IReadOnlyList<TenantSummaryDto>>
{
    public async Task<Result<IReadOnlyList<TenantSummaryDto>>> Handle(ListTenantsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var caller = await users.GetByIdAsync(callerId, cancellationToken).ConfigureAwait(false);
        if (caller is null || !caller.IsPlatformStaff)
        {
            return Error.Forbidden(IdentityErrors.PlatformStaffRequired);
        }

        var all = await tenants.ListAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Select(t => new TenantSummaryDto(t.Id, t.Slug, t.Name, t.Status, t.TrialEndsAt, t.CreatedAt, PlatformTenant.IsPlatform(t.Slug)))
            .ToList();
    }
}
