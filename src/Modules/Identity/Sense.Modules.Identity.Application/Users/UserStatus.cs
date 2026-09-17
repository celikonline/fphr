using FluentValidation;
using Microsoft.Extensions.Logging;
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

/// <summary>
/// Platform (Sense ekibi) bayrağını işaretler/kaldırır. Yalnız bu bayrağa sahip kullanıcılar impersonation
/// isteği/oturumu açabilir (identity.users.impersonate izniyle birlikte); bayrağın kendisi izin vermez.
/// </summary>
[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record SetPlatformStaffCommand(Guid UserId, bool IsPlatformStaff) : ICommand;

public sealed class SetPlatformStaffHandler(IUserRepository users) : ICommandHandler<SetPlatformStaffCommand>
{
    public async Task<Result> Handle(SetPlatformStaffCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        user.SetPlatformStaff(command.IsPlatformStaff);
        return Result.Success();
    }
}

/// <summary>Yönetici MFA sıfırlama (Faz 3 Tur 1, S-12): secret temizlenir + MfaEnabled=false. Kendi hesabına 409
/// (<see cref="IdentityErrors.MfaSelfResetForbidden"/>); farklı kiracı kullanıcısı normal tenant filtresiyle 404
/// olur (<see cref="IUserRepository.GetByIdAsync"/> zaten kiracı bağlamına göre filtreler).
/// G-3-19: MFA gerçekten açıksa (<see cref="User.MfaEnabled"/> veya secret dolu) hedef kullanıcıya güvenlik
/// bildirimi gönderilir - gönderim hatası sıfırlamayı düşürmez, yalnız loglanır.</summary>
[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record ResetUserMfaCommand(Guid UserId) : ICommand;

public sealed class ResetUserMfaHandler(
    IUserRepository users,
    ITenantRepository tenants,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IIdentityMailer mailer,
    ILogger<ResetUserMfaHandler> logger) : ICommandHandler<ResetUserMfaCommand>
{
    public async Task<Result> Handle(ResetUserMfaCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == command.UserId)
        {
            return Error.Conflict(IdentityErrors.MfaSelfResetForbidden);
        }

        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        var wasMfaActive = user.MfaEnabled || user.TotpSecretEncrypted is not null;
        user.DisableMfa();

        if (wasMfaActive)
        {
            try
            {
                var tenantEntity = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
                await mailer.SendMfaResetNoticeAsync(user.Email, user.FullName, tenantEntity?.Name ?? tenant.TenantSlug ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send MFA reset notice to user {UserId}.", user.Id);
            }
        }

        return Result.Success();
    }
}
