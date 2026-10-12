using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Create body of a pay item (ödeme türü). <paramref name="ValidFrom"/> defaults to today.</summary>
public sealed record CreatePayItemRequest(
    string Code,
    string Name,
    PayItemDirection Direction,
    PayItemAmountBasis AmountBasis,
    bool IsSgkSubject,
    bool IsIncomeTaxSubject,
    bool IsStampTaxSubject,
    string Formula,
    decimal? DefaultValue,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    internal PayItemDefinitionInput ToInput() =>
        new(Name, Direction, AmountBasis, IsSgkSubject, IsIncomeTaxSubject, IsStampTaxSubject, Formula, DefaultValue, ValidFrom, ValidTo);
}

/// <summary>Update body: everything but the code.</summary>
public sealed record UpdatePayItemRequest(
    string Name,
    PayItemDirection Direction,
    PayItemAmountBasis AmountBasis,
    bool IsSgkSubject,
    bool IsIncomeTaxSubject,
    bool IsStampTaxSubject,
    string Formula,
    decimal? DefaultValue,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    internal PayItemDefinitionInput ToInput() =>
        new(Name, Direction, AmountBasis, IsSgkSubject, IsIncomeTaxSubject, IsStampTaxSubject, Formula, DefaultValue, ValidFrom, ValidTo);
}

public sealed record ValidateFormulaRequest(string? Formula, PayItemAmountBasis? AmountBasis, Dictionary<string, decimal>? SampleValues);

/// <summary>Bordro > Ödeme türleri (PAY-FORMULA): formula-based earnings. Reads need payroll.trace.view; writes and
/// validate-formula need payroll.payitem.manage (enforced on the requests via [RequiresPermission]).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/pay-items")]
[Authorize]
public sealed class PayItemsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PayItemDefinitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPayItemsQuery(includeInactive), ct));

    [HttpGet("formula-catalog")]
    [ProducesResponseType<FormulaCatalogDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FormulaCatalog(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetFormulaCatalogQuery(), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<PayItemDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetPayItemQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePayItemRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreatePayItemCommand(request.Code, request.ToInput()), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePayItemRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdatePayItemCommand(id, request.ToInput()), ct));

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ActivatePayItemCommand(id), ct));

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivatePayItemCommand(id), ct));

    /// <summary>Checks a formula text and evaluates it with sample values; a bad formula is a normal 200 with isValid=false.</summary>
    [HttpPost("validate-formula")]
    [ProducesResponseType<FormulaValidationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateFormula([FromBody] ValidateFormulaRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ValidatePayItemFormulaQuery(request.Formula, request.AmountBasis, request.SampleValues), ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        var dto = result.Value;
        return Ok(dto.Error is null ? dto : dto with { ErrorMessage = Localizer.Message(dto.Error) });
    }
}
