using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Application.Payments;

/// <summary>Salary history of an employee (newest first, with isCurrent); out-of-scope targets return 404 (IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.SalaryViewKey)]
public sealed record GetEmployeeSalariesQuery(Guid EmployeeId) : IQuery<IReadOnlyList<SalaryRecordDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

public sealed class GetEmployeeSalariesHandler(IEmployeeReadStore store, PaymentsService payments)
    : IQueryHandler<GetEmployeeSalariesQuery, IReadOnlyList<SalaryRecordDto>>
{
    public async Task<Result<IReadOnlyList<SalaryRecordDto>>> Handle(GetEmployeeSalariesQuery query, CancellationToken cancellationToken)
    {
        var employee = await store.GetWithPaymentsAsync(query.EmployeeId, cancellationToken);
        return employee is null ? Error.NotFound(EmployeeErrors.EmployeeNotFound) : Result.Success(payments.Salaries(employee));
    }
}

/// <summary>The caller's own salary history ("Kendi kartım", read-only); 404 when the caller has no linked employee record.</summary>
[RequiresPermission(EmployeePermissions.MyViewKey)]
public sealed record GetMySalariesQuery : IQuery<IReadOnlyList<SalaryRecordDto>>;

public sealed class GetMySalariesHandler(SelfEmployeeResolver self, IEmployeeReadStore store, PaymentsService payments)
    : IQueryHandler<GetMySalariesQuery, IReadOnlyList<SalaryRecordDto>>
{
    public async Task<Result<IReadOnlyList<SalaryRecordDto>>> Handle(GetMySalariesQuery query, CancellationToken cancellationToken)
    {
        var employee = await self.ResolveAsync(cancellationToken) is { } id ? await store.GetWithPaymentsAsync(id, cancellationToken) : null;
        return employee is null ? Error.NotFound(EmployeeErrors.EmployeeNotFound) : Result.Success(payments.Salaries(employee));
    }
}

/// <summary>Adds a salary record ("Maaş bloğu"); returns the updated history.</summary>
[RequiresPermission(EmployeePermissions.SalaryViewKey)]
[RequiresPermission(EmployeePermissions.SalaryEditKey)]
public sealed record AddSalaryRecordCommand(
    Guid EmployeeId,
    DateOnly? EffectiveFrom,
    decimal? Amount,
    string? Currency,
    SalaryType? SalaryType,
    PayFrequency? PayFrequency,
    decimal? TransportAllowance = null,
    decimal? MealAllowance = null,
    string? Note = null) : ICommand<IReadOnlyList<SalaryRecordDto>>, IScopedRequest, ISalaryInput
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Edits a salary record; returns the updated history.</summary>
[RequiresPermission(EmployeePermissions.SalaryViewKey)]
[RequiresPermission(EmployeePermissions.SalaryEditKey)]
public sealed record UpdateSalaryRecordCommand(
    Guid EmployeeId,
    Guid RecordId,
    DateOnly? EffectiveFrom,
    decimal? Amount,
    string? Currency,
    SalaryType? SalaryType,
    PayFrequency? PayFrequency,
    decimal? TransportAllowance = null,
    decimal? MealAllowance = null,
    string? Note = null) : ICommand<IReadOnlyList<SalaryRecordDto>>, IScopedRequest, ISalaryInput
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Deletes a salary record; returns the updated history.</summary>
[RequiresPermission(EmployeePermissions.SalaryViewKey)]
[RequiresPermission(EmployeePermissions.SalaryEditKey)]
public sealed record DeleteSalaryRecordCommand(Guid EmployeeId, Guid RecordId) : ICommand<IReadOnlyList<SalaryRecordDto>>, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>The "Maaş bloğu" fields shared by add/edit.</summary>
public interface ISalaryInput
{
    DateOnly? EffectiveFrom { get; }

    decimal? Amount { get; }

    string? Currency { get; }

    SalaryType? SalaryType { get; }

    PayFrequency? PayFrequency { get; }

    decimal? TransportAllowance { get; }

    decimal? MealAllowance { get; }

    string? Note { get; }
}

/// <summary>
/// "Maaş bloğu bütünlüğü": effective date and amount first - without them nothing else of the block is accepted - then
/// per-field rules (the aggregate re-checks them; these give the client per-field 400s).
/// </summary>
public static class SalaryRules
{
    public static void Apply<T>(AbstractValidator<T> v)
        where T : ISalaryInput
    {
        v.RuleFor(x => x.EffectiveFrom).NotNull().WithMessage(EmployeeDomainErrors.SalaryEffectiveDateRequired);
        v.RuleFor(x => x.Amount).NotNull().WithMessage(EmployeeDomainErrors.SalaryAmountRequired);
        v.When(x => x.EffectiveFrom is not null && x.Amount is not null, () =>
        {
            v.RuleFor(x => x.Amount)
                .Must(amount => SalaryRecord.IsValidAmount(amount!.Value, allowZero: false))
                .WithMessage(EmployeeDomainErrors.SalaryAmountInvalid);
            v.RuleFor(x => x.Currency).NotEmpty().WithMessage(EmployeeDomainErrors.CurrencyRequired);
            v.RuleFor(x => x.Currency)
                .Must(currency => Currencies.IsKnown(SalaryRecord.NormalizeCurrency(currency)))
                .When(x => !string.IsNullOrWhiteSpace(x.Currency))
                .WithMessage(EmployeeDomainErrors.CurrencyInvalid);
            v.RuleFor(x => x.SalaryType).NotNull().WithMessage(EmployeeDomainErrors.SalaryTypeRequired).IsInEnum().WithMessage(EmployeeDomainErrors.SalaryTypeRequired);
            v.RuleFor(x => x.PayFrequency).NotNull().WithMessage(EmployeeDomainErrors.SalaryPayFrequencyRequired).IsInEnum().WithMessage(EmployeeDomainErrors.SalaryPayFrequencyRequired);
            v.RuleFor(x => x.TransportAllowance)
                .Must(value => value is null || SalaryRecord.IsValidAmount(value.Value, allowZero: true))
                .WithMessage(EmployeeDomainErrors.SalaryAllowanceInvalid);
            v.RuleFor(x => x.MealAllowance)
                .Must(value => value is null || SalaryRecord.IsValidAmount(value.Value, allowZero: true))
                .WithMessage(EmployeeDomainErrors.SalaryAllowanceInvalid);
            v.RuleFor(x => x.Note).MaximumLength(EmployeeLimits.SalaryNoteMaxLength).WithMessage(EmployeeDomainErrors.SalaryNoteTooLong);
        });
    }

    public static SalaryRecordData ToData(ISalaryInput input) => new(
        input.EffectiveFrom!.Value,
        input.Amount!.Value,
        input.Currency ?? string.Empty,
        input.SalaryType!.Value,
        input.PayFrequency!.Value,
        input.TransportAllowance,
        input.MealAllowance,
        input.Note);
}

public sealed class AddSalaryRecordValidator : AbstractValidator<AddSalaryRecordCommand>
{
    public AddSalaryRecordValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        SalaryRules.Apply(this);
    }
}

public sealed class UpdateSalaryRecordValidator : AbstractValidator<UpdateSalaryRecordCommand>
{
    public UpdateSalaryRecordValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        SalaryRules.Apply(this);
    }
}

public sealed class DeleteSalaryRecordValidator : AbstractValidator<DeleteSalaryRecordCommand>
{
    public DeleteSalaryRecordValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
    }
}

public sealed class AddSalaryRecordHandler(IEmployeeRepository employees, PaymentsService payments)
    : ICommandHandler<AddSalaryRecordCommand, IReadOnlyList<SalaryRecordDto>>
{
    public async Task<Result<IReadOnlyList<SalaryRecordDto>>> Handle(AddSalaryRecordCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithPaymentsAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var outcome = employee.AddSalaryRecord(SalaryRules.ToData(command));
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        payments.PublishSalaryChanged(employee, outcome.Value);
        return Result.Success(payments.Salaries(employee));
    }
}

public sealed class UpdateSalaryRecordHandler(IEmployeeRepository employees, PaymentsService payments)
    : ICommandHandler<UpdateSalaryRecordCommand, IReadOnlyList<SalaryRecordDto>>
{
    public async Task<Result<IReadOnlyList<SalaryRecordDto>>> Handle(UpdateSalaryRecordCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithPaymentsAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var outcome = employee.UpdateSalaryRecord(command.RecordId, SalaryRules.ToData(command));
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        payments.PublishSalaryChanged(employee, outcome.Value);
        return Result.Success(payments.Salaries(employee));
    }
}

public sealed class DeleteSalaryRecordHandler(IEmployeeRepository employees, PaymentsService payments)
    : ICommandHandler<DeleteSalaryRecordCommand, IReadOnlyList<SalaryRecordDto>>
{
    public async Task<Result<IReadOnlyList<SalaryRecordDto>>> Handle(DeleteSalaryRecordCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetWithPaymentsAsync(command.EmployeeId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var outcome = employee.DeleteSalaryRecord(command.RecordId);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        payments.PublishSalaryChanged(employee, outcome.Value);
        return Result.Success(payments.Salaries(employee));
    }
}
