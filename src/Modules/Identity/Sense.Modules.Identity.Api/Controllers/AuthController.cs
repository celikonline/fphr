using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Invitations;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Application.Users;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public static class IdentityRoutes
{
    public const string Auth = ApiRoutes.VersionedBase + "/identity/auth";
    public const string Tenants = ApiRoutes.VersionedBase + "/identity/tenants";
    public const string Users = ApiRoutes.VersionedBase + "/identity/users";
    public const string Invitations = ApiRoutes.VersionedBase + "/identity/invitations";
    public const string Roles = ApiRoutes.VersionedBase + "/identity/roles";
    public const string Permissions = ApiRoutes.VersionedBase + "/identity/permissions";
    public const string Me = ApiRoutes.VersionedBase + "/identity/me";
    public const string Impersonation = ApiRoutes.VersionedBase + "/identity/impersonation";
    public const string Settings = ApiRoutes.VersionedBase + "/identity/settings";
    public const string UnitAuthorizations = ApiRoutes.VersionedBase + "/identity/unit-authorizations";
}

public sealed record LoginRequest(string Email, string Password, string? TenantSlug);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken, bool AllSessions);

/// <summary>Giriş, token yenileme, çıkış.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Auth)]
public sealed class AuthController : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new LoginCommand(request.Email, request.Password, request.TenantSlug, Request.Headers.UserAgent, HttpContext.Connection.RemoteIpAddress?.ToString()), ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RefreshTokenCommand(request.RefreshToken, Request.Headers.UserAgent, HttpContext.Connection.RemoteIpAddress?.ToString()), ct));

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new LogoutCommand(request.RefreshToken, request.AllSessions), ct));
}

/// <summary>Oturum açmış kullanıcı: profil, roller, izinler, kapsamlar.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Me)]
[Authorize]
public sealed class MeController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => FromResult(await Dispatcher.Query(new GetMeQuery(), ct));
}

public sealed record RegisterTenantRequest(string CompanyName, string Slug, string OwnerEmail, string OwnerFirstName, string OwnerLastName, string Password, string? Phone, string? Language);

/// <summary>Şirket (kiracı) tanımlama: self-signup.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Tenants)]
public sealed class TenantsController : ApiControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<RegisterTenantResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterTenantRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RegisterTenantCommand(request.CompanyName, request.Slug, request.OwnerEmail, request.OwnerFirstName,
            request.OwnerLastName, request.Password, request.Phone, request.Language), ct));
}

public sealed record AcceptInvitationRequest(string Token, string Password, bool PrivacyNoticeAccepted);

public sealed record InviteEmployeeRequest(Guid EmployeeId);

public sealed record BulkInviteEmployeesRequest(Sense.Modules.Employee.Contracts.EmployeeSelection Selection);

/// <summary>Davetler: liste, iptal (yetkili) ve anonim önizleme/kabul.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Invitations)]
public sealed class InvitationsController : ApiControllerBase
{
    [HttpGet]
    [Authorize]
    [ProducesResponseType<Shared.Contracts.Paging.PagedResult<InvitationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Shared.Contracts.Paging.PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListInvitationsQuery(paging), ct));

    [HttpPost(ApiRoutes.IdParam + "/revoke")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new RevokeInvitationCommand(id), ct));

    /// <summary>Çalışana davet (iş e-postası, yoksa kişisel e-posta); hesabı yoksa Employee rolüyle oluşturulur.</summary>
    [HttpPost("employee")]
    [Authorize]
    [ProducesResponseType<EmployeeInvitationResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InviteEmployee([FromBody] InviteEmployeeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new InviteEmployeeCommand(request.EmployeeId), ct));

    /// <summary>Grid seçimindeki çalışanlara toplu davet ("Giriş bilgilerini gönder").</summary>
    [HttpPost("employees/bulk")]
    [Authorize]
    [ProducesResponseType<Shared.Contracts.Bulk.BulkOperationResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkInviteEmployees([FromBody] BulkInviteEmployeesRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new BulkInviteEmployeesCommand(request.Selection), ct));

    [HttpGet("preview/{token}")]
    [AllowAnonymous]
    [ProducesResponseType<InvitationPreviewDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(string token, CancellationToken ct) => FromResult(await Dispatcher.Query(new GetInvitationPreviewQuery(token), ct));

    [HttpPost("accept")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AcceptInvitationCommand(request.Token, request.Password, request.PrivacyNoticeAccepted,
            Request.Headers.UserAgent, HttpContext.Connection.RemoteIpAddress?.ToString()), ct));
}
