using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Absences;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

/// <summary>İzin takvimi: pending/approved leave of the employees the caller may see.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/calendar")]
[Authorize]
public sealed class LeaveCalendarController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LeaveCalendarEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? orgUnitId, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetLeaveCalendarQuery(from, to, orgUnitId), ct)).Localize(localizer));
}
