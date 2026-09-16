using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Expense.Application.ExpenseClaims;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Expense.Api.Controllers;

public sealed record CreateExpenseClaimRequest(string? Description);

public sealed record AddExpenseItemRequest(Guid CategoryId, decimal Amount, DateOnly ReceiptDate, decimal? TaxRate, string Currency, string? ReceiptFileId, string? Description);

public sealed record UpdateExpenseItemRequest(Guid CategoryId, decimal Amount, DateOnly ReceiptDate, decimal? TaxRate, string Currency, string? ReceiptFileId, string? Description);

/// <summary>Multi-receipt expense claims ("Talep > Harcama"). Self-service only in this package - no admin-on-behalf, no approve/pay actions (Workflow governs the decision itself).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/expense/claims")]
[Authorize]
public sealed class ExpenseClaimsController : ApiControllerBase
{
    /// <summary>Lists the caller's own expense claims.</summary>
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<ExpenseClaimDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMy(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyExpenseClaimsQuery(), ct));

    /// <summary>A single expense claim (owner only).</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<ExpenseClaimDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetExpenseClaimQuery(id), ct));

    /// <summary>Creates a Draft expense claim to add receipt items to.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateExpenseClaimRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateExpenseClaimCommand(request.Description), ct));

    /// <summary>Adds a receipt line to a Draft claim; the claim's TotalAmount is recomputed server-side.</summary>
    [HttpPost(ApiRoutes.IdParam + "/items")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AddExpenseItemRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddExpenseItemCommand(id, request.CategoryId, request.Amount, request.ReceiptDate, request.TaxRate, request.Currency, request.ReceiptFileId, request.Description),
            ct));

    /// <summary>Edits a receipt line on a Draft claim.</summary>
    [HttpPut(ApiRoutes.IdParam + "/items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateItem(Guid id, Guid itemId, [FromBody] UpdateExpenseItemRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateExpenseItemCommand(id, itemId, request.CategoryId, request.Amount, request.ReceiptDate, request.TaxRate, request.Currency, request.ReceiptFileId, request.Description),
            ct));

    /// <summary>Removes a receipt line from a Draft claim.</summary>
    [HttpDelete(ApiRoutes.IdParam + "/items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RemoveExpenseItemCommand(id, itemId), ct));

    /// <summary>Submits the claim: Draft -> Submitted, starts the "ExpenseClaim" Workflow approval process.</summary>
    [HttpPost(ApiRoutes.IdParam + "/submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitExpenseClaimCommand(id), ct));
}
