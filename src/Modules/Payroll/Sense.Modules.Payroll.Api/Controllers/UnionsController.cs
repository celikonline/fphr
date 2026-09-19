using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Unions;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

public sealed record UnionRequest(string Name, string? IndustryBranchCode);

/// <summary>Ayarlar > Bordro > "Sendika & TİS" (11.3.07 #58): the tenant's union list. API only - the screen itself
/// is P8. All actions require payroll.union.manage (enforced on the commands/queries via [RequiresPermission]).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/unions")]
[Authorize]
public sealed class UnionsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UnionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListUnionsQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] UnionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateUnionCommand(request.Name, request.IndustryBranchCode), ct));

    [HttpPut("{unionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid unionId, [FromBody] UnionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateUnionCommand(unionId, request.Name, request.IndustryBranchCode), ct));

    [HttpPost("{unionId:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid unionId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateUnionCommand(unionId), ct));
}
