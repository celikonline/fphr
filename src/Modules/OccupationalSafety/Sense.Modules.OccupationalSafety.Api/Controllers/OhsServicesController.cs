using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.OccupationalSafety.Application.Services;
using Sense.Modules.OccupationalSafety.Domain.Services;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.OccupationalSafety.Api.Controllers;

public sealed record CreateSafetyAssignmentRequest(
    Guid LocationId, SafetyServiceRole Role, string PersonName, Guid? EmployeeId, SafetyExpertCertificateClass? CertificateClass,
    string? CertificateNumber, string? ProviderName, int MonthlyMinutes, DateOnly StartDate);

public sealed record AddCommitteeMemberRequest(Guid LocationId, CommitteeRole Role, string PersonName, Guid? EmployeeId, DateOnly StartDate);

public sealed record EndPeriodRequest(DateOnly EndDate);

/// <summary>İSG hizmetleri: uzman/hekim/DSP görevlendirmeleri, İSG kurulu ve lokasyon bazında mevzuat uyumu.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/ohs/services")]
[Authorize]
public sealed class OhsServicesController : ApiControllerBase
{
    [HttpGet("locations")]
    [ProducesResponseType<IReadOnlyList<LocationServiceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summaries(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListLocationServiceSummariesQuery(), ct));

    [HttpGet("assignments")]
    [ProducesResponseType<IReadOnlyList<SafetyAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Assignments(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListSafetyAssignmentsQuery(), ct));

    [HttpPost("assignments")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAssignment([FromBody] CreateSafetyAssignmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateSafetyAssignmentCommand(
            request.LocationId, request.Role, request.PersonName, request.EmployeeId, request.CertificateClass,
            request.CertificateNumber, request.ProviderName, request.MonthlyMinutes, request.StartDate), ct));

    [HttpPost("assignments/" + ApiRoutes.IdParam + "/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EndAssignment(Guid id, [FromBody] EndPeriodRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new EndSafetyAssignmentCommand(id, request.EndDate), ct));

    [HttpGet("committee")]
    [ProducesResponseType<IReadOnlyList<CommitteeMemberDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Committee(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCommitteeMembersQuery(), ct));

    [HttpPost("committee")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMember([FromBody] AddCommitteeMemberRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddCommitteeMemberCommand(request.LocationId, request.Role, request.PersonName, request.EmployeeId, request.StartDate), ct));

    [HttpPost("committee/" + ApiRoutes.IdParam + "/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EndMember(Guid id, [FromBody] EndPeriodRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new EndCommitteeMemberCommand(id, request.EndDate), ct));
}
