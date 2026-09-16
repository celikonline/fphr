using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Expense.Application.Advances;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Expense.Api.Controllers;

public sealed record CreateAdvanceRequestRequest(decimal Amount, string Currency, DateOnly? RequestedDate, bool IsInstallmentRequested, int? InstallmentCount, string? Description);

/// <summary>Advance-payment requests ("Talep > Avans"). Self-service only in this package.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/expense/advances")]
[Authorize]
public sealed class AdvancesController : ApiControllerBase
{
    /// <summary>Lists the caller's own advance requests.</summary>
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<AdvanceRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMy(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyAdvanceRequestsQuery(), ct));

    /// <summary>Submits an advance request; only Amount is strictly required. Starts the "Advance" Workflow approval process.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAdvanceRequestRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateAdvanceRequestCommand(request.Amount, request.Currency, request.RequestedDate, request.IsInstallmentRequested, request.InstallmentCount, request.Description),
            ct));
}
