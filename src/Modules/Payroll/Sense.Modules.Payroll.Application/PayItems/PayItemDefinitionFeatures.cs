using FluentValidation;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.PayItems;

public sealed record PayItemDefinitionDto(
    Guid Id,
    string Code,
    string Name,
    PayItemDirection Direction,
    PayItemAmountBasis AmountBasis,
    bool IsSgkSubject,
    bool IsIncomeTaxSubject,
    bool IsStampTaxSubject,
    string Formula,
    decimal? DefaultValue,
    bool IsActive,
    DateOnly ValidFrom,
    DateOnly? ValidTo);

internal static class PayItemDefinitionMapper
{
    public static PayItemDefinitionDto ToDto(PayItemDefinition d) => new(
        d.Id, d.Code, d.Name, d.Direction, d.AmountBasis, d.IsSgkSubject, d.IsIncomeTaxSubject, d.IsStampTaxSubject,
        d.Formula, d.DefaultValue, d.IsActive, d.ValidFrom, d.ValidTo);
}

[RequiresPermission("payroll.trace.view")]
public sealed record ListPayItemsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<PayItemDefinitionDto>>;

public sealed class ListPayItemsHandler(IPayItemDefinitionRepository definitions) : IQueryHandler<ListPayItemsQuery, IReadOnlyList<PayItemDefinitionDto>>
{
    public async Task<Result<IReadOnlyList<PayItemDefinitionDto>>> Handle(ListPayItemsQuery query, CancellationToken cancellationToken) =>
        (await definitions.ListAsync(query.IncludeInactive, cancellationToken)).Select(PayItemDefinitionMapper.ToDto).ToList();
}

[RequiresPermission("payroll.trace.view")]
public sealed record GetPayItemQuery(Guid Id) : IQuery<PayItemDefinitionDto>;

public sealed class GetPayItemHandler(IPayItemDefinitionRepository definitions) : IQueryHandler<GetPayItemQuery, PayItemDefinitionDto>
{
    public async Task<Result<PayItemDefinitionDto>> Handle(GetPayItemQuery query, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(query.Id, cancellationToken);
        return definition is null ? Error.NotFound(PayrollDomainErrors.PayItemNotFound) : PayItemDefinitionMapper.ToDto(definition);
    }
}

/// <summary>Editable pay item fields as the API receives them; ValidFrom defaults to today (UTC) on create and to the current value on update.</summary>
public sealed record PayItemDefinitionInput(
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
    internal PayItemDefinitionData ToData(DateOnly defaultValidFrom) => new(
        Name, Direction, AmountBasis, IsSgkSubject, IsIncomeTaxSubject, IsStampTaxSubject, Formula, DefaultValue, ValidFrom ?? defaultValidFrom, ValidTo);
}

[RequiresPermission("payroll.payitem.manage")]
public sealed record CreatePayItemCommand(string Code, PayItemDefinitionInput Input) : ICommand<Guid>;

public sealed class CreatePayItemValidator : AbstractValidator<CreatePayItemCommand>
{
    public CreatePayItemValidator() => RuleFor(x => x.Input).NotNull();
}

public sealed class CreatePayItemHandler(ITenantContext tenant, IPayItemDefinitionRepository definitions, TimeProvider clock)
    : ICommandHandler<CreatePayItemCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePayItemCommand command, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var created = PayItemDefinition.Create(tenant.TenantId, command.Code, command.Input.ToData(today));
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (await definitions.CodeExistsAsync(created.Value.Code, cancellationToken))
        {
            return Error.Conflict(PayrollDomainErrors.PayItemCodeTaken);
        }

        definitions.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Edits everything except the code (and, once the item is assigned, its direction). A formula change only affects open/future periods (closed payslips keep the formula text in their trace); payslips of employees assigned this item in open periods are flagged changed.</summary>
[RequiresPermission("payroll.payitem.manage")]
public sealed record UpdatePayItemCommand(Guid Id, PayItemDefinitionInput Input) : ICommand;

public sealed class UpdatePayItemValidator : AbstractValidator<UpdatePayItemCommand>
{
    public UpdatePayItemValidator() => RuleFor(x => x.Input).NotNull();
}

public sealed class UpdatePayItemHandler(
    IPayItemDefinitionRepository definitions, IEmployeePayItemAssignmentRepository assignments, PayItemChangeFlagger flagger)
    : ICommandHandler<UpdatePayItemCommand>
{
    public async Task<Result> Handle(UpdatePayItemCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemNotFound);
        }

        var data = command.Input.ToData(definition.ValidFrom);

        // Earning <-> deduction would make the payslips already calculated with this definition meaningless: locked once it is assigned.
        if (data.Direction != definition.Direction
            && await assignments.ListByDefinitionAsync(definition.Id, DateOnly.MinValue, cancellationToken) is { Count: > 0 })
        {
            return Error.Conflict(PayrollDomainErrors.PayItemDirectionLocked);
        }

        var updated = definition.Update(data);
        if (updated.IsFailure)
        {
            return updated;
        }

        await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        return Result.Success();
    }
}

[RequiresPermission("payroll.payitem.manage")]
public sealed record ActivatePayItemCommand(Guid Id) : ICommand;

public sealed class ActivatePayItemHandler(IPayItemDefinitionRepository definitions, PayItemChangeFlagger flagger) : ICommandHandler<ActivatePayItemCommand>
{
    public async Task<Result> Handle(ActivatePayItemCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemNotFound);
        }

        if (!definition.IsActive)
        {
            definition.Activate();
            await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        }

        return Result.Success();
    }
}

[RequiresPermission("payroll.payitem.manage")]
public sealed record DeactivatePayItemCommand(Guid Id) : ICommand;

public sealed class DeactivatePayItemHandler(IPayItemDefinitionRepository definitions, PayItemChangeFlagger flagger) : ICommandHandler<DeactivatePayItemCommand>
{
    public async Task<Result> Handle(DeactivatePayItemCommand command, CancellationToken cancellationToken)
    {
        var definition = await definitions.FindByIdAsync(command.Id, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemNotFound);
        }

        if (definition.IsActive)
        {
            definition.Deactivate();
            await flagger.FlagDefinitionChangedAsync(definition.Id, cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>
/// Marks employees' payslips "changed" (<see cref="PayslipDirtyFlagReasons.PayItemChanged"/>): the employee of an
/// assignment, or - when a definition changes - everyone whose assignment of it overlaps a period that is still open.
/// </summary>
public sealed class PayItemChangeFlagger(
    ITenantContext tenant, IPayrollPeriodRepository periods, IEmployeePayItemAssignmentRepository assignments, PayslipDirtyFlagService flagging)
{
    public Task FlagEmployeeAsync(Guid employeeId, DateOnly fromMonth, DateOnly? toMonth, CancellationToken cancellationToken) =>
        flagging.FlagAsync(tenant.TenantId, employeeId, fromMonth, PayslipDirtyFlagReasons.PayItemChanged, null, cancellationToken, toMonth);

    public async Task FlagDefinitionChangedAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var open = await periods.ListOpenAsync(cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var earliestOpen = open.Min(p => EmployeePayItemAssignment.FirstOfMonth(p.Year, p.Month));
        var affected = await assignments.ListByDefinitionAsync(definitionId, earliestOpen, cancellationToken);
        foreach (var byEmployee in affected.GroupBy(a => a.EmployeeId))
        {
            var from = byEmployee.Min(a => a.ValidFromMonth);
            DateOnly? through = byEmployee.Any(a => a.ValidToMonth is null) ? null : byEmployee.Max(a => a.ValidToMonth);
            await FlagEmployeeAsync(byEmployee.Key, from, through, cancellationToken);
        }
    }
}
