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

/// <summary>Rol atama (RBAC). Son Owner'ın Owner rolü kaldırılamaz.</summary>
[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : ICommand;

public sealed class SetUserRolesHandler(IUserRepository users, IRoleRepository roles, AccessTypeGuard accessTypeGuard, IPermissionCacheInvalidator cache)
    : ICommandHandler<SetUserRolesCommand>
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

        var allowed = await accessTypeGuard.EnsureCanChangeAsync(user.Roles.Select(r => r.RoleId).ToList(), command.RoleIds, cancellationToken).ConfigureAwait(false);
        if (allowed.IsFailure)
        {
            return allowed.Error;
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

/// <summary>
/// Kullanıcı grubu seçici (çalışan kartı / Kullanıcılar ekranı): İK Yöneticisi / Muhasebe / Çalışan gruplarından yalnız
/// biri kalır, kullanıcının özel rolleri korunur. Ayrı bir izin yerine mevcut UsersManage kullanılır. Hesap sahibi (Owner)
/// seçilemez: İK Yöneticisiyle birlikte taşınır, başka gruba geçen hesap sahibinden düşer — bunu yalnız bir Owner
/// yapabilir (<see cref="AccessTypeGuard"/>) ve son aktif Owner düşürülemez.
/// </summary>
[RequiresPermission(IdentityPermissions.UsersManage)]
public sealed record SetUserAccessTypeCommand(Guid UserId, string AccessType) : ICommand;

public sealed class SetUserAccessTypeValidator : AbstractValidator<SetUserAccessTypeCommand>
{
    public SetUserAccessTypeValidator()
    {
        RuleFor(x => x.AccessType)
            .Must(v => SystemRoleCodes.UserGroups.Contains(v))
            .WithMessage(IdentityErrors.AccessTypeInvalid);
    }
}

public sealed class SetUserAccessTypeHandler(IUserRepository users, IRoleRepository roles, AccessTypeGuard accessTypeGuard, IPermissionCacheInvalidator cache)
    : ICommandHandler<SetUserAccessTypeCommand>
{

    public async Task<Result> Handle(SetUserAccessTypeCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(IdentityErrors.UserNotFound);
        }

        var accessTypeRoles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in SystemRoleCodes.UserGroups)
        {
            var role = await roles.GetByCodeAsync(code, cancellationToken).ConfigureAwait(false);
            if (role is not null)
            {
                accessTypeRoles[code] = role;
            }
        }

        if (!accessTypeRoles.TryGetValue(command.AccessType, out var targetRole))
        {
            return Error.NotFound(IdentityErrors.RoleNotFound);
        }

        var ownerRole = await roles.GetByCodeAsync(SystemRoleCodes.Owner, cancellationToken).ConfigureAwait(false);
        var keepsOwner = string.Equals(command.AccessType, SystemRoleCodes.HR, StringComparison.OrdinalIgnoreCase);
        var accessTypeRoleIds = accessTypeRoles.Values.Select(r => r.Id).ToHashSet();
        var currentRoleIds = user.Roles.Select(r => r.RoleId).ToList();
        var newRoleIds = currentRoleIds
            .Where(id => !accessTypeRoleIds.Contains(id) && (keepsOwner || id != ownerRole?.Id))
            .Append(targetRole.Id)
            .ToList();

        var allowed = await accessTypeGuard.EnsureCanChangeAsync(currentRoleIds, newRoleIds, cancellationToken).ConfigureAwait(false);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var currentlyOwner = ownerRole is not null && user.HasRole(ownerRole.Id);
        if (currentlyOwner && user.Status == UserStatus.Active && !keepsOwner)
        {
            var owners = await users.CountActiveOwnersAsync(cancellationToken).ConfigureAwait(false);
            if (owners <= 1)
            {
                return Error.Rule(IdentityErrors.UserLastOwner);
            }
        }

        user.SetRoles(newRoleIds);
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
