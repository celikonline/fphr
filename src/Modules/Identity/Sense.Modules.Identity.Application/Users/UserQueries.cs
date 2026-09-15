using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Application.Users;

/// <summary>Kullanıcı listesi (grid standardı: sayfalama/sıralama/filtre). Sorgu tarafı Infrastructure'da EF ile projekte edilir.</summary>
[RequiresPermission(IdentityPermissions.UsersView)]
public sealed record ListUsersQuery(PagedQuery Paging) : IQuery<PagedResult<UserListItemDto>>;

[RequiresPermission(IdentityPermissions.UsersView)]
public sealed record GetUserQuery(Guid UserId) : IQuery<UserDetailDto>;

[RequiresPermission(IdentityPermissions.UsersView)]
public sealed record ListInvitationsQuery(PagedQuery Paging) : IQuery<PagedResult<InvitationDto>>;

/// <summary>Okuma tarafı portu: liste sorguları için EF projeksiyonu (Onion: Application tanımlar, Infrastructure implement eder).</summary>
public interface IIdentityReadStore
{
    Task<PagedResult<UserListItemDto>> ListUsersAsync(PagedQuery paging, CancellationToken ct);

    Task<UserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct);

    Task<PagedResult<InvitationDto>> ListInvitationsAsync(PagedQuery paging, CancellationToken ct);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct);

    Task<RoleDto?> GetRoleAsync(Guid roleId, CancellationToken ct);

    /// <summary>Bulunamayan (veya silinmiş) kullanıcılar sözlükte yer almaz.</summary>
    Task<IReadOnlyDictionary<Guid, UserSummaryDto>> GetUserSummariesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);

    /// <summary>Aktif/davetli kullanıcılar; ad, soyad veya e-postada arama.</summary>
    Task<IReadOnlyList<UnitAuthorizationCandidateDto>> ListUnitAuthorizationCandidatesAsync(string? q, int limit, CancellationToken ct);

    /// <summary>Kiracının grantları (yeniden eskiye); platform kullanıcısının e-postası kendi (platform) kiracısından okunur.</summary>
    Task<IReadOnlyList<ImpersonationGrantDto>> ListImpersonationGrantsAsync(Domain.Impersonation.ImpersonationGrantStatus? status, DateTime nowUtc, CancellationToken ct);
}

public sealed class ListUsersHandler(IIdentityReadStore store) : IQueryHandler<ListUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<Shared.Kernel.Results.Result<PagedResult<UserListItemDto>>> Handle(ListUsersQuery query, CancellationToken cancellationToken) =>
        await store.ListUsersAsync(query.Paging, cancellationToken).ConfigureAwait(false);
}

public sealed class GetUserHandler(IIdentityReadStore store) : IQueryHandler<GetUserQuery, UserDetailDto>
{
    public async Task<Shared.Kernel.Results.Result<UserDetailDto>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var dto = await store.GetUserAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        return dto is null ? Shared.Kernel.Results.Error.NotFound(IdentityErrors.UserNotFound) : dto;
    }
}

public sealed class ListInvitationsHandler(IIdentityReadStore store) : IQueryHandler<ListInvitationsQuery, PagedResult<InvitationDto>>
{
    public async Task<Shared.Kernel.Results.Result<PagedResult<InvitationDto>>> Handle(ListInvitationsQuery query, CancellationToken cancellationToken) =>
        await store.ListInvitationsAsync(query.Paging, cancellationToken).ConfigureAwait(false);
}
