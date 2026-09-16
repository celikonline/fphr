using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sense.Modules.Recruitment.Application.Public;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>
/// Request to submit a public career-site application. <c>PrivacyNoticeVersion</c>/<c>KvkkAccepted</c> are the
/// aydınlatma metni acceptance (Faz 9 tur 3 TALIMAT madde 2) - validated and logged only for now, see
/// <see cref="Sense.Modules.Recruitment.Application.Public.SubmitCareerApplicationHandler"/>.
/// </summary>
public sealed record SubmitCareerApplicationRequest(
    string FirstName, string LastName, string Email, string? Phone, string PrivacyNoticeVersion, bool KvkkAccepted);

/// <summary>
/// Public career site (Kolay İK ATS "Kariyer sayfası", R3): unauthenticated postings list/detail + application
/// submission, keyed by a job posting's permanent <c>ApplySlug</c> - except <see cref="List"/> (S-10), the one
/// action keyed by a tenant slug instead, for the top-level <c>/kariyer</c> listing page before any posting is
/// known. No <see cref="AuthorizeAttribute"/> anywhere in this controller - the whole surface is intentionally
/// anonymous and intentionally narrow.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/public/postings")]
public sealed class PublicCareerController : ApiControllerBase
{
    /// <summary>
    /// A tenant's Open, not-yet-expired postings (S-10, Faz 9 gün açılışı TALIMAT'ı madde 2), resolved by its
    /// public-facing <paramref name="tenant"/> slug (never a raw tenant id/claim). Unknown or missing slug: empty
    /// list, never 404 (no tenant-existence signal to an anonymous caller).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("public-anonymous")]
    [ProducesResponseType<IReadOnlyList<PublicJobPostingSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? tenant, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPublicJobPostingsByTenantSlugQuery(tenant), ct));

    /// <summary>An Open posting's public detail by its apply slug.</summary>
    [HttpGet("{applySlug}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anonymous")]
    [ProducesResponseType<PublicJobPostingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string applySlug, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetPublicJobPostingQuery(applySlug), ct));

    /// <summary>The same company's other Open postings ("diğer açık pozisyonlar"), anchored on one posting's slug.</summary>
    [HttpGet("{applySlug}/related")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anonymous")]
    [ProducesResponseType<IReadOnlyList<PublicJobPostingSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Related(string applySlug, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListRelatedPublicJobPostingsQuery(applySlug), ct));

    /// <summary>
    /// Submits a candidate's application for one posting. Rate limit policy name ("public-anonymous-write") is
    /// ADR-0003 (see shared pano B-3); the policy itself (window/limit) is Faz 1's in Program.cs/WebServiceCollectionExtensions.
    /// </summary>
    [HttpPost("{applySlug}/apply")]
    [AllowAnonymous]
    [EnableRateLimiting("public-anonymous-write")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply(string applySlug, [FromBody] SubmitCareerApplicationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new SubmitCareerApplicationCommand(
                applySlug, request.FirstName, request.LastName, request.Email, request.Phone,
                request.PrivacyNoticeVersion, request.KvkkAccepted),
            ct));
}
