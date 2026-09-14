using System.Text.Json;
using FluentValidation;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Identity.Application.Roles;

[RequiresPermission(IdentityPermissions.RolesView)]
public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleDto>>;

[RequiresPermission(IdentityPermissions.RolesView)]
public sealed record GetRoleQuery(Guid RoleId) : IQuery<RoleDto>;

/// <summary>Birleşik izin kataloğu (tüm modüller), modül ve gruba göre; rol editörü matrisi bunu kullanır.</summary>
[RequiresPermission(IdentityPermissions.RolesView)]
public sealed record ListPermissionsQuery : IQuery<IReadOnlyList<PermissionGroupDto>>;

[RequiresPermission(IdentityPermissions.RolesManage)]
public sealed record CreateRoleCommand(string Code, string Name, string? Description, IReadOnlyList<RolePermissionDto> Permissions) : ICommand<Guid>;

[RequiresPermission(IdentityPermissions.RolesManage)]
public sealed record UpdateRoleCommand(Guid RoleId, string Name, string? Description) : ICommand;

[RequiresPermission(IdentityPermissions.RolesManage)]
public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<RolePermissionDto> Permissions) : ICommand;

[RequiresPermission(IdentityPermissions.RolesManage)]
public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(IdentityLimits.RoleCodeMaxLength).Must(Role.IsValidCode).WithMessage(IdentityErrors.RoleInvalidCode);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(IdentityLimits.NameMaxLength);
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class ListRolesHandler(IIdentityReadStore store) : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(ListRolesQuery query, CancellationToken cancellationToken) =>
        Result.Success(await store.ListRolesAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class GetRoleHandler(IIdentityReadStore store) : IQueryHandler<GetRoleQuery, RoleDto>
{
    public async Task<Result<RoleDto>> Handle(GetRoleQuery query, CancellationToken cancellationToken)
    {
        var dto = await store.GetRoleAsync(query.RoleId, cancellationToken).ConfigureAwait(false);
        return dto is null ? Error.NotFound(IdentityErrors.RoleNotFound) : dto;
    }
}

public sealed class ListPermissionsHandler(IPermissionCatalog catalog) : IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionGroupDto>>
{
    public Task<Result<IReadOnlyList<PermissionGroupDto>>> Handle(ListPermissionsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionGroupDto> groups = catalog.All
            .GroupBy(p => (p.Module, p.Group))
            .OrderBy(g => g.Key.Module, StringComparer.Ordinal).ThenBy(g => g.Key.Group, StringComparer.Ordinal)
            .Select(g => new PermissionGroupDto(g.Key.Module, g.Key.Group,
                g.Select(p => new PermissionDto(p.Key, p.Module, p.DisplayName, p.Group, p.Description)).ToList()))
            .ToList();
        return Task.FromResult(Result.Success(groups));
    }
}

public sealed class CreateRoleHandler(IRoleRepository roles, IPermissionCatalog catalog, ITenantContext tenant) : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        if (await roles.CodeExistsAsync(command.Code, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(IdentityErrors.RoleCodeTaken);
        }

        var created = Role.CreateCustom(tenant.TenantId, command.Code, command.Name, command.Description);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var role = created.Value;
        var mapped = RolePermissionMapper.Map(role.Id, command.Permissions, catalog);
        if (mapped.IsFailure)
        {
            return mapped.Error;
        }

        var set = role.SetPermissions(mapped.Value);
        if (set.IsFailure)
        {
            return set.Error;
        }

        roles.Add(role);
        return role.Id;
    }
}

public sealed class UpdateRoleHandler(IRoleRepository roles) : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken).ConfigureAwait(false);
        return role is null ? Error.NotFound(IdentityErrors.RoleNotFound) : role.Rename(command.Name, command.Description);
    }
}

public sealed class SetRolePermissionsHandler(IRoleRepository roles, IPermissionCatalog catalog, IPermissionCacheInvalidator cache, ITenantContext tenant)
    : ICommandHandler<SetRolePermissionsCommand>
{
    public async Task<Result> Handle(SetRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Error.NotFound(IdentityErrors.RoleNotFound);
        }

        var mapped = RolePermissionMapper.Map(role.Id, command.Permissions, catalog);
        if (mapped.IsFailure)
        {
            return mapped.Error;
        }

        var set = role.SetPermissions(mapped.Value);
        if (set.IsFailure)
        {
            return set.Error;
        }

        await cache.InvalidateTenantAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

public sealed class DeleteRoleHandler(IRoleRepository roles, IUserRepository users) : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Error.NotFound(IdentityErrors.RoleNotFound);
        }

        var count = await users.CountByRoleAsync(role.Id, cancellationToken).ConfigureAwait(false);
        var deletable = role.EnsureDeletable(count);
        if (deletable.IsFailure)
        {
            return deletable.Error;
        }

        roles.Remove(role);
        return Result.Success();
    }
}

/// <summary>DTO → domain RolePermission; bilinmeyen izinleri reddeder.</summary>
public static class RolePermissionMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Result<IReadOnlyList<RolePermission>> Map(Guid roleId, IReadOnlyList<RolePermissionDto> dtos, IPermissionCatalog catalog)
    {
        var list = new List<RolePermission>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (!catalog.Exists(dto.Permission))
            {
                return Error.Validation(IdentityErrors.RoleUnknownPermission, (IdentityErrors.Args.Permission, dto.Permission));
            }

            var scopeIds = dto.Scope == DataScope.Custom && dto.ScopeIds is { Count: > 0 } ? JsonSerializer.Serialize(dto.ScopeIds, Json) : null;
            list.Add(new RolePermission(roleId, dto.Permission, (int)dto.Scope, scopeIds));
        }

        return list;
    }

    public static IReadOnlyList<Guid>? ParseScopeIds(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<List<Guid>>(json, Json);
}
