using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

/// <summary>"Ekibim" izinleri (FG-007): pending/approved leave of the caller's direct reports (leave.my.team).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/me/team")]
[Authorize]
public sealed class LeaveMyTeamController : ApiControllerBase
{
    /// <summary>Leave overlapping [from, to] (at most 93 days); <paramref name="status"/> = pending | approved narrows the list.</summary>
    [HttpGet("requests")]
    [ProducesResponseType<IReadOnlyList<LeaveCalendarEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests(
        [FromServices] IErrorLocalizer localizer, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] LeaveRequestStatus? status,
        CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new ListMyTeamLeaveRequestsQuery(from, to, status), ct)).Localize(localizer));
}
