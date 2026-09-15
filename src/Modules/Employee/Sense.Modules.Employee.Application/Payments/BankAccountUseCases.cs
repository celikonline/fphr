using FluentValidation;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Payments;

/// <summary>Bank accounts of an employee (primary first); IBAN masked without employee.sensitive.view; out-of-scope targets return 404.</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record GetEmployeeBankAccountsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<BankAccountDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class GetEmployeeBankAccountsHandler(IEmployeeReadStore store, PaymentsService payments, EmployeeSensitiveAccess sensitiveAccess)
    : IQueryHandler<GetEmployeeBankAccountsQuery, IReadOnlyList<BankAccountDto>>
{
    public async Task<Result<IReadOnlyList<BankAccountDto>>> Handle(GetEmployeeBankAccountsQuery query, CancellationToken cancellationToken)
    {
        var employee = await store.GetWithPaymentsAsync(query.EmployeeId, cancellationToken);
        return employee is null
            ? Error.NotFound(EmployeeErrors.EmployeeNotFound)
            : Result.Success(payments.BankAccounts(employee, await sensitiveAccess.CanViewAsync(cancellationToken)));
    }
}

/// <summary>The caller's own bank accounts (read-only, IBAN unmasked: it is their own data); 404 without a linked employee record.</summary>
[RequiresPermission(EmployeePermissions.MyViewKey)]
public sealed record GetMyBankAccountsQuery : IQuery<IReadOnlyList<BankAccountDto>>;

public sealed class GetMyBankAccountsHandler(SelfEmployeeResolver self, IEmployeeReadStore store, PaymentsService payments)
    : IQueryHandler<GetMyBankAccountsQuery, IReadOnlyList<BankAccountDto>>
{
    public async Task<Result<IReadOnlyList<BankAccountDto>>> Handle(GetMyBankAccountsQuery query, CancellationToken cancellationToken)
    {
        var employee = await self.ResolveAsync(cancellationToken) is { } id ? await store.GetWithPaymentsAsync(id, cancellationToken) : null;
        return employee is null ? Error.NotFound(EmployeeErrors.EmployeeNotFound) : Result.Success(payments.BankAccounts(employee, unmasked: true));
    }
}

/// <summary>Adds a bank account (the first one, or one sent with isPrimary, becomes primary); returns the updated list.</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
[RequiresPermission(EmployeePermissions.BankAccountsEditKey)]
public sealed record AddBankAccountCommand(
    Guid EmployeeId,
    string? Iban,
    BankAccountType? AccountType,
    string? BankName = null,
    string? Currency = null,
    string? Note = null,
    bool IsPrimary = false) : ICommand<IReadOnlyList<BankAccountDto>>, IScopedRequest, IBankAccountInput
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Edits a bank account; a null IBAN keeps the stored one. The primary flag changes only through <see cref="SetPrimaryBankAccountCommand"/>.</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
[RequiresPermission(EmployeePermissions.BankAccountsEditKey)]
public sealed record UpdateBankAccountCommand(
    Guid EmployeeId,
    Guid AccountId,
    string? Iban,
    BankAccountType? AccountType,
    string? BankName = null,
    string? Currency = null,
    string? Note = null) : ICommand<IReadOnlyList<BankAccountDto>>, IScopedRequest, IBankAccountInput
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Deletes a bank account (a deleted primary hands the flag to the oldest remaining account).</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
[RequiresPermission(EmployeePermissions.BankAccountsEditKey)]
public sealed record DeleteBankAccountCommand(Guid EmployeeId, Guid AccountId) : ICommand<IReadOnlyList<BankAccountDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Makes an account the employee's single primary account.</summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
[RequiresPermission(EmployeePermissions.BankAccountsEditKey)]
public sealed record SetPrimaryBankAccountCommand(Guid EmployeeId, Guid AccountId) : ICommand<IReadOnlyList<BankAccountDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>The bank account fields shared by add/edit.</summary>
public interface IBankAccountInput
{
    string? Iban { get; }

    BankAccountType? AccountType { get; }

    string? BankName { get; }

    string? Currency { get; }

    string? Note { get; }
}

/// <summary>Field rules shared by add/edit (the aggregate re-checks them; these give the client per-field 400s).</summary>
public static class BankAccountRules
{
    public static void Apply<T>(AbstractValidator<T> v, bool ibanRequired)
        where T : IBankAccountInput
    {
        if (ibanRequired)
        {
            v.RuleFor(x => x.Iban).NotEmpty().WithMessage(EmployeeDomainErrors.IbanRequired);
        }

        v.RuleFor(x => x.Iban).Custom((iban, context) =>
        {
            if (!string.IsNullOrWhiteSpace(iban) && BankAccount.ParseIban(iban) is { IsFailure: true } parsed)
            {
                context.AddFailure(nameof(IBankAccountInput.Iban), parsed.Error.Code);
            }
        });
        v.RuleFor(x => x.AccountType).NotNull().WithMessage(EmployeeDomainErrors.BankAccountTypeRequired).IsInEnum().WithMessage(EmployeeDomainErrors.BankAccountTypeRequired);
        v.RuleFor(x => x.BankName).MaximumLength(EmployeeLimits.BankNameMaxLength).WithMessage(EmployeeDomainErrors.BankNameTooLong);
        v.RuleFor(x => x.Currency)
            .Must(currency => Currencies.IsKnown(SalaryRecord.NormalizeCurrency(currency)))
            .When(x => !string.IsNullOrWhiteSpace(x.Currency))
            .WithMessage(EmployeeDomainErrors.CurrencyInvalid);
        v.RuleFor(x => x.Note).MaximumLength(EmployeeLimits.BankAccountNoteMaxLength).WithMessage(EmployeeDomainErrors.BankAccountNoteTooLong);
    }

    /// <summary>Blank IBAN on an edit means "keep the stored one".</summary>
    public static BankAccountData ToData(IBankAccountInput input) => new(
        string.IsNullOrWhiteSpace(input.Iban) ? null : input.Iban,
        input.AccountType!.Value,
        input.BankName,
        input.Currency,
        input.Note);
}

public sealed class AddBankAccountValidator : AbstractValidator<AddBankAccountCommand>
{
    public AddBankAccountValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        BankAccountRules.Apply(this, ibanRequired: true);
    }
}

public sealed class UpdateBankAccountValidator : AbstractValidator<UpdateBankAccountCommand>
{
    public UpdateBankAccountValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        BankAccountRules.Apply(this, ibanRequired: false);
    }
}

public sealed class DeleteBankAccountValidator : AbstractValidator<DeleteBankAccountCommand>
{
    public DeleteBankAccountValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
    }
}

public sealed class SetPrimaryBankAccountValidator : AbstractValidator<SetPrimaryBankAccountCommand>
{
    public SetPrimaryBankAccountValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
    }
}

public sealed class AddBankAccountHandler(IEmployeeRepository employees, BankAccountWriter writer)
    : ICommandHandler<AddBankAccountCommand, IReadOnlyList<BankAccountDto>>
{
    public Task<Result<IReadOnlyList<BankAccountDto>>> Handle(AddBankAccountCommand command, CancellationToken cancellationToken) =>
        writer.ApplyAsync(employees, command.EmployeeId, (employee, protector) =>
        {
            var added = employee.AddBankAccount(BankAccountRules.ToData(command), command.IsPrimary, protector);
            return added.IsFailure ? added.Error : Result.Success(true);
        }, cancellationToken);
}

public sealed class UpdateBankAccountHandler(IEmployeeRepository employees, BankAccountWriter writer)
    : ICommandHandler<UpdateBankAccountCommand, IReadOnlyList<BankAccountDto>>
{
    public Task<Result<IReadOnlyList<BankAccountDto>>> Handle(UpdateBankAccountCommand command, CancellationToken cancellationToken) =>
        writer.ApplyAsync(employees, command.EmployeeId, (employee, protector) =>
            employee.UpdateBankAccount(command.AccountId, BankAccountRules.ToData(command), protector), cancellationToken);
}

public sealed class DeleteBankAccountHandler(IEmployeeRepository employees, BankAccountWriter writer)
    : ICommandHandler<DeleteBankAccountCommand, IReadOnlyList<BankAccountDto>>
{
    public Task<Result<IReadOnlyList<BankAccountDto>>> Handle(DeleteBankAccountCommand command, CancellationToken cancellationToken) =>
        writer.ApplyAsync(employees, command.EmployeeId, (employee, _) =>
        {
            var deleted = employee.DeleteBankAccount(command.AccountId);
            return deleted.IsFailure ? deleted.Error : Result.Success(true);
        }, cancellationToken);
}

public sealed class SetPrimaryBankAccountHandler(IEmployeeRepository employees, BankAccountWriter writer)
    : ICommandHandler<SetPrimaryBankAccountCommand, IReadOnlyList<BankAccountDto>>
{
    public Task<Result<IReadOnlyList<BankAccountDto>>> Handle(SetPrimaryBankAccountCommand command, CancellationToken cancellationToken) =>
        writer.ApplyAsync(employees, command.EmployeeId, (employee, _) => employee.SetPrimaryBankAccount(command.AccountId), cancellationToken);
}

/// <summary>Load → mutate → EmployeeProfileChanged("bank") when something changed → list (masked per the caller's sensitive.view).</summary>
public sealed class BankAccountWriter(IIbanProtector protector, PaymentsService payments, EmployeeSensitiveAccess sensitiveAccess)
{
    public async Task<Result<IReadOnlyList<BankAccountDto>>> ApplyAsync(
        IEmployeeRepository employees,
        Guid employeeId,
        Func<EmployeeEntity, IIbanProtector, Result<bool>> mutate,
        CancellationToken ct)
    {
        var found = await employees.GetWithPaymentsAsync(employeeId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var changed = mutate(employee, protector);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (changed.Value)
        {
            payments.PublishBankChanged(employee);
        }

        return Result.Success(payments.BankAccounts(employee, await sensitiveAccess.CanViewAsync(ct).ConfigureAwait(false)));
    }
}
