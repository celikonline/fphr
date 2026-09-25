using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.MonthlyTimesheet;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>Aylık puantaj matrisi (Z3.4): çalışan × gün durum kodu; sunucu ayın her gününü üretir.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/monthly-matrix")]
[Authorize]
public sealed class MonthlyMatrixController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<MonthlyMatrixDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly period, [FromQuery] Guid? orgUnitId, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = MonthlyMatrixLimits.DefaultPageSize) =>
        FromResult(await Dispatcher.Query(new GetMonthlyMatrixQuery(period, orgUnitId, page, pageSize), ct));
}
