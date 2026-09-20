using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Invitations;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain.Roles;
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
    public const string AuditLog = ApiRoutes.VersionedBase + "/identity/audit-log";
}

public sealed record LoginRequest(string Email, string Password, string? TenantSlug);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken, bool AllSessions);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record MfaCompleteRequest(string MfaToken, string Code);

public sealed record MfaCodeRequest(string Code);

/// <summary>Giriş, token yenileme, çıkış.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Auth)]
public sealed class AuthController : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-attempt")]
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

    /// <summary>Var olmayan e-posta için de aynı (204) yanıt döner — kullanıcı numaralandırmasına izin verilmez.</summary>
    /// <remarks>
    /// Rate limit politika adı ("public-anonymous-write") ADR-0003 ile PO tarafından karara bağlandı; politikanın
    /// kendisi (pencere/limit) Faz 1'in Program.cs'inde tanımlanır (bkz. paylaşılan pano B-3). Bu öznitelik o politika
    /// henüz tanımlanmamışken de zararsızdır: `UseRateLimiter()` devreye girmeden `[EnableRateLimiting]` yalnızca
    /// endpoint metadata'sı ekler, çalışma zamanında hiçbir şeyi değiştirmez.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anonymous-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ForgotPasswordCommand(request.Email), ct));

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anonymous-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ResetPasswordCommand(request.Token, request.NewPassword), ct));

    /// <summary>Login'in `{ status: "mfa_required", mfaToken }` yanıtından sonraki adım: TOTP kodu ile oturum tamamlanır.</summary>
    [HttpPost("mfa/complete")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anonymous-write")]
    [ProducesResponseType<LoginResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteMfa([FromBody] MfaCompleteRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CompleteMfaLoginCommand(request.MfaToken, request.Code, Request.Headers.UserAgent, HttpContext.Connection.RemoteIpAddress?.ToString()), ct));
}

/// <summary>Oturum açmış kullanıcı: profil, roller, izinler, kapsamlar.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Me)]
[Authorize]
public sealed class MeController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<MeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetMeQuery(), ct);
        return result.IsSuccess ? Ok(Localize(result.Value)) : Problem(result.Error);
    }

    /// <summary>Sistem rolü adlarını RolesController.Localize ile aynı kaynak anahtarına (roles.{code}.name) bağlar,
    /// böylece "Profilim" ekranı Kullanıcılar → Roller listesiyle aynı Türkçe rol adını gösterir.</summary>
    private MeDto Localize(MeDto me) => me with
    {
        Roles = me.Roles
            .Select(r => SystemRoleCodes.All.Contains(r.Code) || SystemRoleCodes.PlatformOnly.Contains(r.Code)
                ? r with { Name = Localizer.MessageOrText(SystemRoleCodes.NameKey(r.Code)) }
                : r)
            .ToList(),
    };

    /// <summary>Güvenlik ekranı: MFA açık mı, yarım kalmış kayıt var mı.</summary>
    [HttpGet("mfa")]
    [ProducesResponseType<MfaStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMfaStatus(CancellationToken ct) => FromResult(await Dispatcher.Query(new GetMfaStatusQuery(), ct));

    /// <summary>MFA kaydı 1. adım: secret üretilir ve şifreli yazılır, ama etkinleşmez (bkz. <see cref="EnrollMfaCommand"/>). MFA zaten açıksa 409.</summary>
    [HttpPost("mfa/enroll")]
    [ProducesResponseType<MfaEnrollmentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EnrollMfa(CancellationToken ct) => FromResult(await Dispatcher.Send(new EnrollMfaCommand(), ct));

    /// <summary>MFA kaydı 2. adım: kod doğrulanınca etkinleşir.</summary>
    [HttpPost("mfa/verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> VerifyMfa([FromBody] MfaCodeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new VerifyMfaEnrollmentCommand(request.Code), ct));

    /// <summary>Self-servis MFA kapatma (kod ile onay). Yönetici sıfırlaması için ayrı bir uç var (Users → mfa/reset).</summary>
    [HttpPost("mfa/disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisableMfa([FromBody] MfaCodeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DisableMfaCommand(request.Code), ct));
}

/// <param name="OwnerMode"><c>invite</c>: Owner davet e-postasıyla parolasını kendisi belirler; <c>password</c>: <paramref name="Password"/> ile doğrudan etkin açılır.</param>
public sealed record CreateTenantRequest(
    string CompanyName, string Slug, string OwnerEmail, string OwnerFirstName, string OwnerLastName,
    TenantOwnerMode OwnerMode, string? Password, string? Phone, string? Language);

/// <summary>
/// Şirket (kiracı) yönetimi — yalnız süper admin (platform kiracısındaki <c>IsPlatformStaff</c> kullanıcı, <c>platform.tenants.manage</c>).
/// Herkese açık self-signup kaldırıldı.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Tenants)]
[Authorize]
public sealed class TenantsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TenantSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTenantsQuery(), ct));

    [HttpPost]
    [ProducesResponseType<CreateTenantResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateTenantCommand(request.CompanyName, request.Slug, request.OwnerEmail, request.OwnerFirstName,
            request.OwnerLastName, request.OwnerMode, request.Password, request.Phone, request.Language), ct));
}

public sealed record AcceptInvitationRequest(string Token, string Password, bool PrivacyNoticeAccepted, string? PrivacyNoticeVersion);

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
        FromResult(await Dispatcher.Send(new AcceptInvitationCommand(request.Token, request.Password, request.PrivacyNoticeAccepted, request.PrivacyNoticeVersion,
            Request.Headers.UserAgent, HttpContext.Connection.RemoteIpAddress?.ToString()), ct));
}
