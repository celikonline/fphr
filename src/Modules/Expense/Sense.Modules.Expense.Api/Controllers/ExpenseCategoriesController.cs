using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Expense.Application.Categories;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Expense.Api.Controllers;

public sealed record CreateExpenseCategoryRequest(string Code, string NameKey, decimal? DefaultTaxRate);

public sealed record SetExpenseCategoryActiveRequest(bool IsActive);

/// <summary>Expense category catalog (seeded per tenant: "expense.category.travel"/"expense.category.food").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/expense/categories")]
[Authorize]
public sealed class ExpenseCategoriesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ExpenseCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListExpenseCategoriesQuery(includeInactive), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateExpenseCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateExpenseCategoryCommand(request.Code, request.NameKey, request.DefaultTaxRate), ct));

    [HttpPut(ApiRoutes.IdParam + "/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetExpenseCategoryActiveRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetExpenseCategoryActiveCommand(id, request.IsActive), ct));
}
