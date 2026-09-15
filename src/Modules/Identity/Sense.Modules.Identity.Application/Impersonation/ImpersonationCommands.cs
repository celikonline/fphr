using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Impersonation;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Impersonation;

internal static class ImpersonationAudit
{
    public const string Resource = "tenant";
    public const string Requested = "impersonation.requested";
    public const string Approved = "impersonation.approved";
    public const string Started = "impersonation.started";
    public const string Revoked = "impersonation.revoked";
}

/// <summary>
/// Platform personelinin bir kiracıyı impersonate etme talebi (gerekçe zorunlu). identity.users.impersonate
/// izni gerekir (hiçbir kiracı-taraf sistem rolüne verilmez); ayrıca çağıran kullanıcının kendi (platform)
/// kiracısındaki User.IsPlatformStaff bayrağı true olmalıdır — Owner/Admin joker izin setine sahip olduğu için
/// izin kontrolü tek başına yeterli değildir.
/// </summary>
[RequiresPermission(IdentityPermissions.UsersImpersonate)]
public sealed record RequestImpersonationCommand(Guid TenantId, string Reason) : ICommand<Guid>;

public sealed class RequestImpersonationValidator : AbstractValidator<RequestImpersonationCommand>
{
    public RequestImpersonationValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(IdentityLimits.ReasonMaxLength).WithMessage(IdentityErrors.ImpersonationReasonRequired);
    }
}

public sealed class RequestImpersonationHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IImpersonationGrantRepository grants,
    ITenantContextSetter tenantSetter,
    ICurrentUser currentUser,
    INotificationService notifications,
    TimeProvider clock) : ICommandHandler<RequestImpersonationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RequestImpersonationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } platformUserId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        // Çağıranın kendi (platform) kiracısındaki kaydı: ambient tenant context henüz değiştirilmedi.
        var platformUser = await users.GetByIdAsync(platformUserId, cancellationToken).ConfigureAwait(false);
        if (platformUser is null || !platformUser.IsPlatformStaff)
        {
            return Error.Forbidden(IdentityErrors.ImpersonationRequiresPlatformStaff);
        }

        using var scope = tenantSetter.BeginScope(command.TenantId);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var created = ImpersonationGrant.Create(command.TenantId, platformUserId, command.Reason, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var grant = created.Value;
        await grants.AddAndSaveAsync(grant, cancellationToken).ConfigureAwait(false);

        await notifications.BroadcastAuditAlertAsync(command.TenantId,
            AuditAlertDto.Create(ImpersonationAudit.Requested, ImpersonationAudit.Resource, platformUser.Email, command.Reason))
            .ConfigureAwait(false);

        return grant.Id;
    }
}

/// <summary>Kiracı tarafı onay: yalnız Owner/Admin (identity.impersonation.approve, Owner/Admin joker izin setine dahildir).</summary>
[RequiresPermission(IdentityPermissions.ImpersonationApprove)]
public sealed record ApproveImpersonationCommand(Guid GrantId) : ICommand;

public sealed class ApproveImpersonationHandler(
    IImpersonationGrantRepository grants,
    ICurrentUser currentUser,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ICommandHandler<ApproveImpersonationCommand>
{
    public async Task<Result> Handle(ApproveImpersonationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } approverId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        var grant = await grants.GetByIdAsync(command.GrantId, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return Error.NotFound(IdentityErrors.ImpersonationNotFound);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var window = TimeSpan.FromHours(options.Value.ImpersonationWindowHours);
        return grant.Approve(approverId, now, window);
    }
}

/// <summary>Onaylı ve süresi geçmemiş bir grant ile impersonation oturumu başlatır: token üretilir (`act` claim'i ile).</summary>
[RequiresPermission(IdentityPermissions.UsersImpersonate)]
public sealed record StartImpersonationCommand(Guid TenantId, Guid GrantId) : ICommand<ImpersonationSessionDto>;

public sealed class StartImpersonationHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IImpersonationGrantRepository grants,
    ITenantContextSetter tenantSetter,
    ICurrentUser currentUser,
    ITokenService tokens,
    INotificationService notifications,
    TimeProvider clock) : ICommandHandler<StartImpersonationCommand, ImpersonationSessionDto>
{
    public async Task<Result<ImpersonationSessionDto>> Handle(StartImpersonationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } platformUserId)
        {
            return Error.Unauthorized(ErrorCodes.Unauthenticated);
        }

        // Token'a gömülecek profil bilgileri (email/isim/dil), hedef kiracıya geçmeden önce kendi (platform) kaydından okunur.
        var platformUser = await users.GetByIdAsync(platformUserId, cancellationToken).ConfigureAwait(false);
        if (platformUser is null || !platformUser.IsPlatformStaff)
        {
            return Error.Forbidden(IdentityErrors.ImpersonationRequiresPlatformStaff);
        }

        using var scope = tenantSetter.BeginScope(command.TenantId);

        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound(IdentityErrors.TenantNotFound);
        }

        var grant = await grants.GetByIdAsync(command.GrantId, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return Error.NotFound(IdentityErrors.ImpersonationNotFound);
        }

        if (grant.PlatformUserId != platformUserId)
        {
            return Error.Forbidden(IdentityErrors.ImpersonationForbidden);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var started = grant.Start(now);
        if (started.IsFailure)
        {
            return started.Error;
        }

        var access = tokens.IssueAccessToken(platformUser, tenant.Id, tenant.Slug, roleCodes: [], permissionVersion: 0,
            impersonatorUserId: platformUserId, maxExpiresAtUtc: grant.ExpiresAt);

        await notifications.BroadcastAuditAlertAsync(command.TenantId,
            AuditAlertDto.Create(ImpersonationAudit.Started, ImpersonationAudit.Resource, platformUser.Email, grant.Reason))
            .ConfigureAwait(false);

        return new ImpersonationSessionDto(access.Token, access.ExpiresAtUtc);
    }
}

/// <summary>Onay bekleyen ya da onaylanmış (henüz kullanılmamış/süregelen) bir grantı erken sonlandırır.</summary>
[RequiresPermission(IdentityPermissions.ImpersonationApprove)]
public sealed record RevokeImpersonationCommand(Guid GrantId) : ICommand;

public sealed class RevokeImpersonationHandler(IImpersonationGrantRepository grants, TimeProvider clock) : ICommandHandler<RevokeImpersonationCommand>
{
    public async Task<Result> Handle(RevokeImpersonationCommand command, CancellationToken cancellationToken)
    {
        var grant = await grants.GetByIdAsync(command.GrantId, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return Error.NotFound(IdentityErrors.ImpersonationNotFound);
        }

        return grant.Revoke(clock.GetUtcNow().UtcDateTime);
    }
}

/// <summary>Ayarlar &gt; Destek erişimi: kiracının grant listesi, isteğe bağlı durum filtresiyle (Pending|Approved|Active|Expired|Revoked).</summary>
[RequiresPermission(IdentityPermissions.ImpersonationApprove)]
public sealed record ListImpersonationGrantsQuery(string? Status) : IQuery<IReadOnlyList<ImpersonationGrantDto>>
{
    public static bool TryParseStatus(string? value, out ImpersonationGrantStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse<ImpersonationGrantStatus>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            status = parsed;
            return true;
        }

        return false;
    }
}

public sealed class ListImpersonationGrantsValidator : AbstractValidator<ListImpersonationGrantsQuery>
{
    public ListImpersonationGrantsValidator() =>
        RuleFor(x => x.Status).Must(s => ListImpersonationGrantsQuery.TryParseStatus(s, out _)).WithMessage(IdentityErrors.ImpersonationStatusInvalid);
}

public sealed class ListImpersonationGrantsHandler(Users.IIdentityReadStore store, TimeProvider clock)
    : IQueryHandler<ListImpersonationGrantsQuery, IReadOnlyList<ImpersonationGrantDto>>
{
    public async Task<Result<IReadOnlyList<ImpersonationGrantDto>>> Handle(ListImpersonationGrantsQuery query, CancellationToken cancellationToken)
    {
        ListImpersonationGrantsQuery.TryParseStatus(query.Status, out var status);
        return Result.Success(await store.ListImpersonationGrantsAsync(status, clock.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false));
    }
}
