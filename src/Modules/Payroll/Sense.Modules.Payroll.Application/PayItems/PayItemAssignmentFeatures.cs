using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.PayItems;

public sealed record PayItemAssignmentDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    Guid PayItemDefinitionId,
    string Code,
    string Name,
    decimal? Value,
    int FromYear,
    int FromMonth,
    int? ToYear,
    int? ToMonth);

public sealed record CreatePayItemAssignmentsResult(IReadOnlyList<Guid> CreatedIds);

/// <summary>Months whose payroll period is no longer Open (Approved/Paid/Locked): assignments may not touch them.</summary>
internal static class ClosedMonths
{
    public static async Task<IReadOnlySet<DateOnly>> LoadAsync(IPayrollPeriodRepository periods, CancellationToken cancellationToken) =>
        (await periods.ListAllAsync(cancellationToken))
            .Where(p => p.Status != PayrollPeriodStatus.Open)
            .Select(p => EmployeePayItemAssignment.FirstOfMonth(p.Year, p.Month))
            .ToHashSet();
}

internal static class PayItemAssignmentInput
{
    public static DateOnly? ToMonth(int? year, int? month) => year is { } y && month is { } m ? EmployeePayItemAssignment.FirstOfMonth(y, m) : null;

    public static bool IsValidMonthPair(int? year, int? month) =>
        (year is null) == (month is null) && (month is null || month is >= 1 and <= 12) && (year is null || year is >= 1 and <= 9999);
}

/// <summary>Assignments of one employee, or covering one payroll period's month (at least one filter is required).</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ListPayItemAssignmentsQuery(Guid? EmployeeId, Guid? PeriodId) : IQuery<IReadOnlyList<PayItemAssignmentDto>>;

public sealed class ListPayItemAssignmentsValidator : AbstractValidator<ListPayItemAssignmentsQuery>
{
    public ListPayItemAssignmentsValidator() =>
        RuleFor(x => x).Must(x => x.EmployeeId is not null || x.PeriodId is not null).WithName("employeeId or periodId");
}

public sealed class ListPayItemAssignmentsHandler(
    IPayrollPeriodRepository periods,
    IEmployeePayItemAssignmentRepository assignments,
    IPayItemDefinitionRepository definitions,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant) : IQueryHandler<ListPayItemAssignmentsQuery, IReadOnlyList<PayItemAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<PayItemAssignmentDto>>> Handle(ListPayItemAssignmentsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmployeePayItemAssignment> list;
        if (query.PeriodId is { } periodId)
        {
            var period = await periods.FindByIdAsync(periodId, cancellationToken);
            if (period is null)
            {
                return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
            }

            list = await assignments.ListCoveringMonthAsync(EmployeePayItemAssignment.FirstOfMonth(period.Year, period.Month), cancellationToken);
            if (query.EmployeeId is { } onlyEmployee)
            {
                list = list.Where(a => a.EmployeeId == onlyEmployee).ToList();
            }
        }
        else
        {
            list = await assignments.ListByEmployeeAsync(query.EmployeeId!.Value, cancellationToken);
        }

        var defs = (await definitions.ListByIdsAsync(list.Select(a => a.PayItemDefinitionId).Distinct().ToList(), cancellationToken)).ToDictionary(d => d.Id);
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, list.Select(a => a.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        IReadOnlyList<PayItemAssignmentDto> dtos = list
            .Where(a => defs.ContainsKey(a.PayItemDefinitionId))
            .Select(a =>
            {
                var definition = defs[a.PayItemDefinitionId];
                var contact = contacts.GetValueOrDefault(a.EmployeeId);
                var name = contact is null ? a.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
                return new PayItemAssignmentDto(
                    a.Id, a.EmployeeId, name, a.PayItemDefinitionId, definition.Code, definition.Name, a.Value,
                    a.ValidFromMonth.Year, a.ValidFromMonth.Month, a.ValidToMonth?.Year, a.ValidToMonth?.Month);
            }).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Assigns one pay item to 1..500 employees for a month range; all-or-nothing. Refused for closed periods, inactive items, a missing <c>deger</c> and a 51st item in a month.</summary>
[RequiresPermission("payroll.payitem.manage")]
public sealed record CreatePayItemAssignmentsCommand(
    Guid PayItemDefinitionId, IReadOnlyList<Guid> EmployeeIds, decimal? Value, int FromYear, int FromMonth, int? ToYear, int? ToMonth)
    : ICommand<CreatePayItemAssignmentsResult>;

public sealed class CreatePayItemAssignmentsValidator : AbstractValidator<CreatePayItemAssignmentsCommand>
{
    public CreatePayItemAssignmentsValidator()
    {
        RuleFor(x => x.PayItemDefinitionId).NotEmpty();
        RuleFor(x => x.EmployeeIds).NotEmpty();
        RuleFor(x => x.FromMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.FromYear).InclusiveBetween(1, 9999);
        RuleFor(x => x).Must(x => PayItemAssignmentInput.IsValidMonthPair(x.ToYear, x.ToMonth)).WithName("toYear/toMonth");
    }
}

public sealed class CreatePayItemAssignmentsHandler(
    ITenantContext tenant,
    IPayrollPeriodRepository periods,
    IPayItemDefinitionRepository definitions,
    IEmployeePayItemAssignmentRepository assignments,
    IEmployeeDirectory employeeDirectory,
    PayItemChangeFlagger flagger) : ICommandHandler<CreatePayItemAssignmentsCommand, CreatePayItemAssignmentsResult>
{
    public async Task<Result<CreatePayItemAssignmentsResult>> Handle(CreatePayItemAssignmentsCommand command, CancellationToken cancellationToken)
    {
        var employeeIds = command.EmployeeIds.Distinct().ToList();
        if (employeeIds.Count > PayrollLimits.MaxPayItemAssignmentEmployees)
        {
            return Error.Validation(PayrollDomainErrors.PayItemAssignmentTooMany);
        }

        var definition = await definitions.FindByIdAsync(command.PayItemDefinitionId, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemNotFound);
        }

        var contacts = await employeeDirectory.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken);
        if (contacts.Select(c => c.EmployeeId).Distinct().Count() != employeeIds.Count)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemEmployeeNotFound);
        }

        var from = EmployeePayItemAssignment.FirstOfMonth(command.FromYear, command.FromMonth);
        var to = PayItemAssignmentInput.ToMonth(command.ToYear, command.ToMonth);
        var closed = await ClosedMonths.LoadAsync(periods, cancellationToken);
        var existing = (await assignments.ListByEmployeesAsync(employeeIds, cancellationToken)).ToLookup(a => a.EmployeeId);

        var created = new List<EmployeePayItemAssignment>();
        foreach (var employeeId in employeeIds)
        {
            var assignment = EmployeePayItemAssignment.Create(tenant.TenantId, employeeId, definition, command.Value, from, to, closed);
            if (assignment.IsFailure)
            {
                return assignment.Error;
            }

            if (EmployeePayItemAssignment.OverlapsSameItem(existing[employeeId], definition.Id, assignment.Value.ValidFromMonth, assignment.Value.ValidToMonth))
            {
                return Error.Conflict(PayrollDomainErrors.PayItemAssignmentOverlap);
            }

            if (EmployeePayItemAssignment.ExceedsMonthlyLimit(existing[employeeId].ToList(), assignment.Value.ValidFromMonth, assignment.Value.ValidToMonth))
            {
                return Error.Validation(PayrollDomainErrors.PayItemAssignmentTooMany);
            }

            created.Add(assignment.Value);
        }

        foreach (var assignment in created)
        {
            assignments.Add(assignment);
            await flagger.FlagEmployeeAsync(assignment.EmployeeId, assignment.ValidFromMonth, assignment.ValidToMonth, cancellationToken);
        }

        return new CreatePayItemAssignmentsResult(created.Select(a => a.Id).ToList());
    }
}

/// <summary>Changes the value and/or end month of an assignment. A used (closed-period) assignment can only be ended, not re-valued.</summary>
[RequiresPermission("payroll.payitem.manage")]
public sealed record UpdatePayItemAssignmentCommand(Guid Id, decimal? Value, int? ToYear, int? ToMonth) : ICommand;

public sealed class UpdatePayItemAssignmentValidator : AbstractValidator<UpdatePayItemAssignmentCommand>
{
    public UpdatePayItemAssignmentValidator() =>
        RuleFor(x => x).Must(x => PayItemAssignmentInput.IsValidMonthPair(x.ToYear, x.ToMonth)).WithName("toYear/toMonth");
}

public sealed class UpdatePayItemAssignmentHandler(
    IPayrollPeriodRepository periods,
    IPayItemDefinitionRepository definitions,
    IEmployeePayItemAssignmentRepository assignments,
    PayItemChangeFlagger flagger) : ICommandHandler<UpdatePayItemAssignmentCommand>
{
    public async Task<Result> Handle(UpdatePayItemAssignmentCommand command, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindByIdAsync(command.Id, cancellationToken);
        if (assignment is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemAssignmentNotFound);
        }

        var definition = await definitions.FindByIdAsync(assignment.PayItemDefinitionId, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemNotFound);
        }

        var oldTo = assignment.ValidToMonth;
        var newTo = PayItemAssignmentInput.ToMonth(command.ToYear, command.ToMonth);
        var closed = await ClosedMonths.LoadAsync(periods, cancellationToken);
        var sameEmployee = await assignments.ListByEmployeeAsync(assignment.EmployeeId, cancellationToken);
        if (EmployeePayItemAssignment.OverlapsSameItem(sameEmployee, assignment.PayItemDefinitionId, assignment.ValidFromMonth, newTo, assignment.Id))
        {
            return Error.Conflict(PayrollDomainErrors.PayItemAssignmentOverlap);
        }

        var updated = assignment.Update(definition, command.Value, newTo, closed);
        if (updated.IsFailure)
        {
            return updated;
        }

        DateOnly? through = oldTo is null || newTo is null ? null : oldTo > newTo ? oldTo : newTo;
        await flagger.FlagEmployeeAsync(assignment.EmployeeId, assignment.ValidFromMonth, through, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Deletes an assignment no closed period has used; otherwise end it with an end month instead.</summary>
[RequiresPermission("payroll.payitem.manage")]
public sealed record DeletePayItemAssignmentCommand(Guid Id) : ICommand;

public sealed class DeletePayItemAssignmentHandler(
    IPayrollPeriodRepository periods,
    IEmployeePayItemAssignmentRepository assignments,
    PayItemChangeFlagger flagger) : ICommandHandler<DeletePayItemAssignmentCommand>
{
    public async Task<Result> Handle(DeletePayItemAssignmentCommand command, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindByIdAsync(command.Id, cancellationToken);
        if (assignment is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemAssignmentNotFound);
        }

        var deletable = assignment.EnsureDeletable(await ClosedMonths.LoadAsync(periods, cancellationToken));
        if (deletable.IsFailure)
        {
            return deletable;
        }

        assignments.Remove(assignment);
        await flagger.FlagEmployeeAsync(assignment.EmployeeId, assignment.ValidFromMonth, assignment.ValidToMonth, cancellationToken);
        return Result.Success();
    }
}
