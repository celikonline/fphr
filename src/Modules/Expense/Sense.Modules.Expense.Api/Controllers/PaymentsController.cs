using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Expense.Application.Payments;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Expense.Api.Controllers;

/// <summary>"Kendi hesabım > Ödemeler" unified self-service overview (expense + advance rows; Overtime/Adjustment rows land in a later package).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/expense/payments")]
[Authorize]
public sealed class PaymentsController : ApiControllerBase
{
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<PaymentOverviewRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMy(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyPaymentsOverviewQuery(), ct));
}
