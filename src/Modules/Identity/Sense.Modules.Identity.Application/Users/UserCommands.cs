using FluentValidation;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Users;

/// <summary>Kullanıcı tanımlama: kayıt oluşturulur (Invited), roller atanır, istenirse davet e-postası gönderilir.</summary>
[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record CreateUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    IReadOnlyList<Guid> RoleIds,
    Guid? EmployeeId,
    bool SendInvitation,
    string? Language) : ICommand<Guid>;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.RoleIds).NotNull();
    }
}

public sealed class CreateUserHandler(
    IUserRepository users,
    IRoleRepository roles,
    ITenantContext tenant,
    IDispatcher dispatcher,
    IOptions<IdentityOptions> options) : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        if (await users.EmailExistsAsync(command.Email, null, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(IdentityErrors.UserEmailTaken);
        }

        var roleEntities = await roles.GetByIdsAsync(command.RoleIds, cancellationToken).ConfigureAwait(false);
        if (roleEntities.Count != command.RoleIds.Distinct().Count())
        {
            return Error.NotFound(IdentityErrors.RoleNotFound);
        }

        var created = User.Create(tenant.TenantId, command.Email, command.FirstName, command.LastName,
            string.IsNullOrWhiteSpace(command.Language) ? options.Value.DefaultLanguage : command.Language, command.Phone);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var user = created.Value;
        user.SetRoles(command.RoleIds);
        if (command.EmployeeId is { } employeeId)
        {
            user.LinkEmployee(employeeId);
        }

        users.Add(user);

        if (command.SendInvitation)
        {
            var invite = await dispatcher.Send(new SendInvitationCommand(user.Id), cancellationToken).ConfigureAwait(false);
            if (invite.IsFailure)
            {
                return invite.Error;
            }
        }

        return user.Id;
    }
}

[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record UpdateUserCommand(Guid UserId, string FirstName, string LastName, string? Phone, string? Language, Guid? EmployeeId) : ICommand;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
    }
}

public sealed class UpdateUserHandler(IUserRepository users) : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        user.UpdateProfile(command.FirstName, command.LastName, command.Phone, command.Language);
        if (command.EmployeeId is { } employeeId)
        {
            user.LinkEmployee(employeeId);
        }
        else
        {
            user.UnlinkEmployee();
        }

        return Result.Success();
    }
}

/// <summary>Rol atama (RBAC). Son Owner'ın Owner rolü kaldırılamaz.</summary>
[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : ICommand;

public sealed class SetUserRolesHandler(IUserRepository users, IRoleRepository roles, IPermissionCacheInvalidator cache) : ICommandHandler<SetUserRolesCommand>
{
    public async Task<Result> Handle(SetUserRolesCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        var target = await roles.GetByIdsAsync(command.RoleIds, cancellationToken).ConfigureAwait(false);
        if (target.Count != command.RoleIds.Distinct().Count())
        {
            return Error.NotFound(IdentityErrors.RoleNotFound);
        }

        var ownerRole = await roles.GetByCodeAsync(SystemRoleCodes.Owner, cancellationToken).ConfigureAwait(false);
        if (ownerRole is not null && user.HasRole(ownerRole.Id) && !command.RoleIds.Contains(ownerRole.Id))
        {
            var owners = await users.CountActiveOwnersAsync(cancellationToken).ConfigureAwait(false);
            if (owners <= 1)
            {
                return Error.Rule(IdentityErrors.UserLastOwner);
            }
        }

        user.SetRoles(command.RoleIds);
        await cache.InvalidateUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record SetUserPermissionOverrideCommand(Guid UserId, string Permission, bool? Allow) : ICommand;

public sealed class SetUserPermissionOverrideHandler(IUserRepository users, IPermissionCatalog catalog, IPermissionCacheInvalidator cache)
    : ICommandHandler<SetUserPermissionOverrideCommand>
{
    public async Task<Result> Handle(SetUserPermissionOverrideCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        if (!catalog.Exists(command.Permission))
        {
            return Error.Validation(IdentityErrors.RoleUnknownPermission, (IdentityErrors.Args.Permission, command.Permission));
        }

        if (command.Allow is { } allow)
        {
            user.SetPermissionOverride(command.Permission, allow);
        }
        else
        {
            user.RemovePermissionOverride(command.Permission);
        }

        await cache.InvalidateUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Askıya alma / etkinleştirme. Kendi hesabı ve son Owner korunur.</summary>
[RequiresPermission(IdentityPermissions.UsersSuspend)]
public sealed record SetUserStatusCommand(Guid UserId, bool Enabled) : ICommand;

public sealed class SetUserStatusHandler(IUserRepository users, IRoleRepository roles, ICurrentUser currentUser, IPermissionCacheInvalidator cache)
    : ICommandHandler<SetUserStatusCommand>
{
    public async Task<Result> Handle(SetUserStatusCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == command.UserId)
        {
            return Error.Rule(IdentityErrors.UserCannotModifySelf);
        }

        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        if (command.Enabled)
        {
            var enabled = user.Enable();
            if (enabled.IsFailure)
            {
                return enabled.Error;
            }
        }
        else
        {
            var ownerRole = await roles.GetByCodeAsync(SystemRoleCodes.Owner, cancellationToken).ConfigureAwait(false);
            if (ownerRole is not null && user.HasRole(ownerRole.Id)
                && await users.CountActiveOwnersAsync(cancellationToken).ConfigureAwait(false) <= 1)
            {
                return Error.Rule(IdentityErrors.UserLastOwner);
            }

            user.Disable();
        }

        await cache.InvalidateUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

[RequiresPermission(IdentityPermissions.UsersResetMfa)]
public sealed record ResetUserMfaCommand(Guid UserId) : ICommand;

public sealed class ResetUserMfaHandler(IUserRepository users) : ICommandHandler<ResetUserMfaCommand>
{
    public async Task<Result> Handle(ResetUserMfaCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        user.DisableMfa();
        return Result.Success();
    }
}

/// <summary>Davet gönderme / yeniden gönderme: token üretilir, e-posta yollanır.</summary>
[RequiresPermission(IdentityPermissions.UsersInvite)]
public sealed record SendInvitationCommand(Guid UserId) : ICommand<Guid>;

public sealed class SendInvitationHandler(
    IUserRepository users,
    ITenantRepository tenants,
    IInvitationRepository invitations,
    ISecretGenerator secrets,
    IIdentityMailer mailer,
    IFrontendLinks links,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ICommandHandler<SendInvitationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SendInvitationCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        if (user.Status != UserStatus.Invited)
        {
            return Error.Rule(IdentityErrors.InvitationUserActive);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var validity = TimeSpan.FromHours(options.Value.InvitationValidityHours);
        var rawToken = secrets.NewToken();
        var hash = secrets.Hash(rawToken);

        var invitation = await invitations.GetPendingByUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            invitation = Invitation.Create(tenant.TenantId, user.Id, user.Email, hash, now, validity, currentUser.UserId);
            invitations.Add(invitation);
        }
        else
        {
            var renewed = invitation.Renew(hash, now, validity);
            if (renewed.IsFailure)
            {
                return renewed.Error;
            }
        }

        var tenantEntity = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        await mailer.SendInvitationAsync(user.Email, user.FullName, tenantEntity?.Name ?? tenant.TenantSlug ?? string.Empty,
            links.InviteAccept(rawToken), invitation.ExpiresAt, cancellationToken).ConfigureAwait(false);

        return invitation.Id;
    }
}

[RequiresPermission(IdentityPermissions.UsersInvite)]
public sealed record RevokeInvitationCommand(Guid InvitationId) : ICommand;

public sealed class RevokeInvitationHandler(IInvitationRepository invitations) : ICommandHandler<RevokeInvitationCommand>
{
    public async Task<Result> Handle(RevokeInvitationCommand command, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByIdAsync(command.InvitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        invitation.Revoke();
        return Result.Success();
    }
}

/// <summary>Anonim: davet bağlantısındaki token ile davet önizlemesi.</summary>
public sealed record GetInvitationPreviewQuery(string Token) : IQuery<InvitationPreviewDto>;

public sealed class GetInvitationPreviewHandler(IInvitationRepository invitations, IUserRepository users, ITenantRepository tenants, ISecretGenerator secrets, ITenantContextSetter tenantSetter, TimeProvider clock)
    : IQueryHandler<GetInvitationPreviewQuery, InvitationPreviewDto>
{
    public async Task<Result<InvitationPreviewDto>> Handle(GetInvitationPreviewQuery query, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByTokenHashAsync(secrets.Hash(query.Token), cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        using var scope = tenantSetter.BeginScope(invitation.TenantId);
        var user = await users.GetByIdAsync(invitation.UserId, cancellationToken).ConfigureAwait(false);
        var tenant = await tenants.GetByIdAsync(invitation.TenantId, cancellationToken).ConfigureAwait(false);
        if (user is null || tenant is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        return new InvitationPreviewDto(user.Email, user.FirstName, user.LastName, tenant.Name, tenant.Slug, invitation.ExpiresAt,
            invitation.IsUsable(clock.GetUtcNow().UtcDateTime));
    }
}

/// <summary>Anonim: daveti kabul et, parola belirle, KVKK onayı kaydet, hesabı etkinleştir.</summary>
public sealed record AcceptInvitationCommand(string Token, string Password, bool PrivacyNoticeAccepted, string? DeviceInfo, string? IpAddress) : ICommand<AuthTokensDto>;

public sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator(IOptions<IdentityOptions> options)
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(options.Value.MinPasswordLength).WithMessage(IdentityErrors.PasswordTooShort);
        RuleFor(x => x.PrivacyNoticeAccepted).Equal(true);
    }
}

public sealed class AcceptInvitationHandler(
    IInvitationRepository invitations,
    IUserRepository users,
    ITenantRepository tenants,
    IRoleRepository roles,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    ISecretGenerator secrets,
    ITokenService tokens,
    ITenantContextSetter tenantSetter,
    TimeProvider clock) : ICommandHandler<AcceptInvitationCommand, AuthTokensDto>
{
    public async Task<Result<AuthTokensDto>> Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invitation = await invitations.GetByTokenHashAsync(secrets.Hash(command.Token), cancellationToken).ConfigureAwait(false);
        if (invitation is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        using var scope = tenantSetter.BeginScope(invitation.TenantId);

        var accepted = invitation.Accept(now);
        if (accepted.IsFailure)
        {
            return accepted.Error;
        }

        var user = await users.GetByIdAsync(invitation.UserId, cancellationToken).ConfigureAwait(false);
        var tenant = await tenants.GetByIdAsync(invitation.TenantId, cancellationToken).ConfigureAwait(false);
        if (user is null || tenant is null)
        {
            return Error.NotFound(IdentityErrors.InvitationNotFound);
        }

        user.SetPassword(hasher.Hash(command.Password));
        user.RecordSuccessfulLogin(now);

        var roleCodes = (await roles.GetByIdsAsync(user.Roles.Select(r => r.RoleId), cancellationToken).ConfigureAwait(false)).Select(r => r.Code).ToList();
        var access = tokens.IssueAccessToken(user, tenant.Id, tenant.Slug, roleCodes, permissionVersion: 0);
        var rawRefresh = secrets.NewToken();
        var refresh = Domain.Tokens.RefreshToken.Issue(tenant.Id, user.Id, null, secrets.Hash(rawRefresh), now, tokens.RefreshTokenLifetime, command.DeviceInfo, command.IpAddress);
        refreshTokens.Add(refresh);

        return new AuthTokensDto(access.Token, access.ExpiresAtUtc, rawRefresh, refresh.ExpiresAt);
    }
}
