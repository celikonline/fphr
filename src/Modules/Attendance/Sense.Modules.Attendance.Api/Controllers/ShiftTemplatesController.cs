using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.ShiftTemplates;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record CreateShiftTemplateRequest(string Name, int PeriodLengthDays, IReadOnlyList<ShiftTemplateEntryDto> Entries);

public sealed record ApplyShiftTemplateRequest(DateOnly StartDate, bool OverwritePublished);

/// <summary>Vardiya planı şablonları (Z3.2): kaydet, listele, uygula, sil. İzin: attendance.shifttemplate.manage.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/shift-templates")]
[Authorize]
public sealed class ShiftTemplatesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ShiftTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListShiftTemplatesQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateShiftTemplateRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateShiftTemplateCommand(request.Name, request.PeriodLengthDays, request.Entries), ct));

    /// <summary>Şablonu startDate'ten itibaren uygular; kilitli/yayınlanmış hücreler <c>skipped[]</c> içinde döner (reason: locked | published).</summary>
    [HttpPost("{id:guid}/apply")]
    [ProducesResponseType<ShiftTemplateApplyResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply(Guid id, [FromBody] ApplyShiftTemplateRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ApplyShiftTemplateCommand(id, request.StartDate, request.OverwritePublished), ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteShiftTemplateCommand(id), ct));
}
