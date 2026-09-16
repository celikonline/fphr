using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sense.Modules.Recruitment.Application.Public;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to submit a public career-site application.</summary>
public sealed record SubmitCareerApplicationRequest(string FirstName, string LastName, string Email, string? Phone);

/// <summary>
/// Public career site (Kolay İK ATS "Kariyer sayfası", R3): unauthenticated postings list/detail + application
/// submission, keyed by a job posting's permanent <c>ApplySlug</c>. No <see cref="AuthorizeAttribute"/> anywhere in
/// this controller - the whole surface is intentionally anonymous and intentionally narrow (three actions only).
/// Never accepts or echoes a tenant id/slug: the posting's slug alone resolves the tenant server-side.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/public/postings")]
public sealed class PublicCareerController : ApiControllerBase
{
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
            new SubmitCareerApplicationCommand(applySlug, request.FirstName, request.LastName, request.Email, request.Phone), ct));
}
