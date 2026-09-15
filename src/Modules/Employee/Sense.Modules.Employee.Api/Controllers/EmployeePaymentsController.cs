using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>Add/edit body of a salary record; effective date and amount are mandatory (nothing else is accepted without them).</summary>
public sealed record SalaryRecordRequest(
    DateOnly? EffectiveFrom,
    decimal? Amount,
    string? Currency,
    SalaryType? SalaryType,
    PayFrequency? PayFrequency,
    decimal? TransportAllowance,
    decimal? MealAllowance,
    string? Note);

/// <summary>Add body of a bank account (accountType as a camelCase enum string; IBAN with or without spaces).</summary>
public sealed record AddBankAccountRequest(
    string? Iban,
    BankAccountType? AccountType,
    string? BankName,
    string? Currency,
    string? Note,
    bool IsPrimary = false);

/// <summary>Edit body of a bank account; omitted/blank IBAN keeps the stored one.</summary>
public sealed record UpdateBankAccountRequest(
    string? Iban,
    BankAccountType? AccountType,
    string? BankName,
    string? Currency,
    string? Note);

/// <summary>The employee card's "Ödemeler" tab (E3): salary history and bank accounts. Every write returns the updated list.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employees")]
[Authorize]
public sealed class EmployeePaymentsController : ApiControllerBase
{
    private const string Salaries = "/salaries";
    private const string BankAccounts = "/bank-accounts";
    private const string RecordId = "/{recordId:guid}";
    private const string AccountId = "/{accountId:guid}";

    /// <summary>The caller's own salary history (read-only).</summary>
    [HttpGet("me" + Salaries)]
    [ProducesResponseType<IReadOnlyList<SalaryRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MySalaries(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMySalariesQuery(), ct));

    /// <summary>The caller's own bank accounts (read-only, unmasked).</summary>
    [HttpGet("me" + BankAccounts)]
    [ProducesResponseType<IReadOnlyList<BankAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyBankAccounts(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyBankAccountsQuery(), ct));

    /// <summary>Salary history, newest first (isCurrent = in effect today).</summary>
    [HttpGet(ApiRoutes.IdParam + Salaries)]
    [ProducesResponseType<IReadOnlyList<SalaryRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSalaries(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeSalariesQuery(id), ct));

    [HttpPost(ApiRoutes.IdParam + Salaries)]
    [ProducesResponseType<IReadOnlyList<SalaryRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddSalary(Guid id, [FromBody] SalaryRecordRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddSalaryRecordCommand(id, request.EffectiveFrom, request.Amount, request.Currency, request.SalaryType, request.PayFrequency,
                request.TransportAllowance, request.MealAllowance, request.Note),
            ct));

    [HttpPut(ApiRoutes.IdParam + Salaries + RecordId)]
    [ProducesResponseType<IReadOnlyList<SalaryRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSalary(Guid id, Guid recordId, [FromBody] SalaryRecordRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateSalaryRecordCommand(id, recordId, request.EffectiveFrom, request.Amount, request.Currency, request.SalaryType, request.PayFrequency,
                request.TransportAllowance, request.MealAllowance, request.Note),
            ct));

    [HttpDelete(ApiRoutes.IdParam + Salaries + RecordId)]
    [ProducesResponseType<IReadOnlyList<SalaryRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSalary(Guid id, Guid recordId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteSalaryRecordCommand(id, recordId), ct));

    /// <summary>Bank accounts, primary first (IBAN masked without employee.sensitive.view).</summary>
    [HttpGet(ApiRoutes.IdParam + BankAccounts)]
    [ProducesResponseType<IReadOnlyList<BankAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBankAccounts(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeBankAccountsQuery(id), ct));

    [HttpPost(ApiRoutes.IdParam + BankAccounts)]
    [ProducesResponseType<IReadOnlyList<BankAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddBankAccount(Guid id, [FromBody] AddBankAccountRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddBankAccountCommand(id, request.Iban, request.AccountType, request.BankName, request.Currency, request.Note, request.IsPrimary),
            ct));

    [HttpPut(ApiRoutes.IdParam + BankAccounts + AccountId)]
    [ProducesResponseType<IReadOnlyList<BankAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateBankAccount(Guid id, Guid accountId, [FromBody] UpdateBankAccountRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateBankAccountCommand(id, accountId, request.Iban, request.AccountType, request.BankName, request.Currency, request.Note),
            ct));

    [HttpDelete(ApiRoutes.IdParam + BankAccounts + AccountId)]
    [ProducesResponseType<IReadOnlyList<BankAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteBankAccount(Guid id, Guid accountId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteBankAccountCommand(id, accountId), ct));

    /// <summary>Makes the account the employee's primary account.</summary>
    [HttpPost(ApiRoutes.IdParam + BankAccounts + AccountId + "/primary")]
    [ProducesResponseType<IReadOnlyList<BankAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPrimaryBankAccount(Guid id, Guid accountId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetPrimaryBankAccountCommand(id, accountId), ct));
}
