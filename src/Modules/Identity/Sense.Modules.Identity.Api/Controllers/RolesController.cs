using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record CreateRoleRequest(string Code, string Name, string? Description, IReadOnlyList<RolePermissionDto> Permissions);

public sealed record UpdateRoleRequest(string Name, string? Description);

public sealed record SetRolePermissionsRequest(IReadOnlyList<RolePermissionDto> Permissions);

/// <summary>RBAC: roller ve izin matrisi. Sistem rollerinin adı/açıklaması istek kültürüne göre yerelleştirilir.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Roles)]
[Authorize]
public sealed class RolesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ListRolesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value.Select(Localize).ToList()) : Problem(result.Error);
    }

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetRoleQuery(id), ct);
        return result.IsSuccess ? Ok(Localize(result.Value)) : Problem(result.Error);
    }

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(new CreateRoleCommand(request.Code, request.Name, request.Description, request.Permissions), ct);
        return Created(result, nameof(Get), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateRoleCommand(id, request.Name, request.Description), ct));

    [HttpPut(ApiRoutes.IdParam + "/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] SetRolePermissionsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetRolePermissionsCommand(id, request.Permissions), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new DeleteRoleCommand(id), ct));

    private RoleDto Localize(RoleDto role) => role.IsSystem
        ? role with { Name = Localizer.MessageOrText(SystemRoleCodes.NameKey(role.Code)), Description = Localizer.MessageOrText(SystemRoleCodes.DescriptionKey(role.Code)) }
        : role;
}

/// <summary>Birleşik izin kataloğu (tüm modüller), yerelleştirilmiş ad ve grup ile.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Permissions)]
[Authorize]
public sealed class PermissionsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PermissionGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ListPermissionsQuery(), ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        var localized = result.Value.Select(g => g with
        {
            Group = Localizer.MessageOrText(g.Group),
            Permissions = g.Permissions.Select(p => p with { DisplayName = Localizer.MessageOrText(p.DisplayName), Group = Localizer.MessageOrText(p.Group) }).ToList(),
        }).ToList();
        return Ok(localized);
    }
}
