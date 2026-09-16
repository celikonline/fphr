using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record CreateUserRequest(string Email, string FirstName, string LastName, string? Phone, IReadOnlyList<Guid> RoleIds, Guid? EmployeeId, bool SendInvitation, string? Language);

public sealed record UpdateUserRequest(string FirstName, string LastName, string? Phone, string? Language, Guid? EmployeeId);

public sealed record SetRolesRequest(IReadOnlyList<Guid> RoleIds);

public sealed record SetAccessTypeRequest(string AccessType);

public sealed record SetStatusRequest(bool Enabled);

public sealed record PermissionOverrideRequest(string Permission, bool? Allow);

/// <summary>Kullanıcı tanımlama ve yönetimi (grid standardı: ?page&amp;pageSize&amp;sort&amp;q&amp;status=…).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Users)]
[Authorize]
public sealed class UsersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<UserListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListUsersQuery(paging), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<UserDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Query(new GetUserQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(new CreateUserCommand(request.Email, request.FirstName, request.LastName, request.Phone,
            request.RoleIds, request.EmployeeId, request.SendInvitation, request.Language), ct);
        return Created(result, nameof(Get), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateUserCommand(id, request.FirstName, request.LastName, request.Phone, request.Language, request.EmployeeId), ct));

    [HttpPut(ApiRoutes.IdParam + "/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetRoles(Guid id, [FromBody] SetRolesRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetUserRolesCommand(id, request.RoleIds), ct));

    /// <summary>Erişim türü hızlı seçici: Employee/Owner/Admin sistem rollerinden yalnız biri kalır (diğer roller korunur).</summary>
    [HttpPut(ApiRoutes.IdParam + "/access-type")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetAccessType(Guid id, [FromBody] SetAccessTypeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetUserAccessTypeCommand(id, request.AccessType), ct));

    [HttpPut(ApiRoutes.IdParam + "/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetStatusRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetUserStatusCommand(id, request.Enabled), ct));

    [HttpPut(ApiRoutes.IdParam + "/permission-overrides")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPermissionOverride(Guid id, [FromBody] PermissionOverrideRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetUserPermissionOverrideCommand(id, request.Permission, request.Allow), ct));

    /// <summary>Yönetici MFA sıfırlama (S-12): izin identity.users.manage; kendi hesabına 409, farklı kiracı kullanıcısına 404.</summary>
    [HttpPost(ApiRoutes.IdParam + "/mfa/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetMfa(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new ResetUserMfaCommand(id), ct));

    [HttpPost(ApiRoutes.IdParam + "/invite")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invite(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new SendInvitationCommand(id), ct));
}
