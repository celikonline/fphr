using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Incentives;

public sealed record IncentiveAssignmentDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    Guid IncentiveDefinitionId,
    string Code,
    string Name,
    string? DeclarationCode,
    IncentiveKind Kind,
    bool IsSmmmApproved,
    int FromYear,
    int FromMonth,
    int? ToYear,
    int? ToMonth,
    string? Note,
    bool IsDefinitionActive,
    DateOnly DefinitionValidFrom,
    DateOnly? DefinitionValidTo);

public sealed record CreateIncentiveAssignmentsResult(IReadOnlyList<Guid> CreatedIds);

/// <summary>Resolves the category of the definition behind an assignment (the domain's overlap rule needs it).</summary>
internal static class IncentiveCategories
{
    private const IncentiveCategory Unknown = (IncentiveCategory)(-1);

    public static async Task<Func<Guid, IncentiveCategory>> LoadAsync(
        IIncentiveDefinitionRepository definitions, IEnumerable<EmployeeIncentiveAssignment> assignments, IncentiveDefinition extra, CancellationToken cancellationToken)
    {
        var ids = assignments.Select(a => a.IncentiveDefinitionId).Append(extra.Id).Distinct().ToList();
        var byId = (await definitions.ListByIdsAsync(ids, cancellationToken)).ToDictionary(d => d.Id, d => d.Category);
        byId[extra.Id] = extra.Category;
        return id => byId.GetValueOrDefault(id, Unknown);
    }
}

/// <summary>
/// Marks employees' payslips "changed" (<see cref="PayslipDirtyFlagReasons.IncentiveChanged"/>): the employee of an
/// assignment, or - when a definition changes - everyone whose assignment of it reaches a period that is still open.
/// </summary>
public sealed class IncentiveChangeFlagger(
    ITenantContext tenant, IPayrollPeriodRepository periods, IEmployeeIncentiveAssignmentRepository assignments, PayslipDirtyFlagService flagging)
{
    public Task FlagEmployeeAsync(Guid employeeId, DateOnly fromMonth, DateOnly? toMonth, CancellationToken cancellationToken) =>
        flagging.FlagAsync(tenant.TenantId, employeeId, fromMonth, PayslipDirtyFlagReasons.IncentiveChanged, null, cancellationToken, toMonth);

    public async Task FlagDefinitionChangedAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var open = await periods.ListOpenAsync(cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var earliestOpen = open.Min(p => EmployeeIncentiveAssignment.FirstOfMonth(p.Year, p.Month));
        var affected = await assignments.ListByDefinitionAsync(definitionId, earliestOpen, cancellationToken);
        foreach (var byEmployee in affected.GroupBy(a => a.EmployeeId))
        {
            var from = byEmployee.Min(a => a.ValidFromMonth);
            DateOnly? through = byEmployee.Any(a => a.ValidToMonth is null) ? null : byEmployee.Max(a => a.ValidToMonth);
            await FlagEmployeeAsync(byEmployee.Key, from, through, cancellationToken);
        }
    }
}

/// <summary>Assignments of one employee, or covering one payroll period's month (at least one filter is required).</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ListIncentiveAssignmentsQuery(Guid? EmployeeId, Guid? PeriodId) : IQuery<IReadOnlyList<IncentiveAssignmentDto>>;

public sealed class ListIncentiveAssignmentsValidator : AbstractValidator<ListIncentiveAssignmentsQuery>
{
    public ListIncentiveAssignmentsValidator() =>
        RuleFor(x => x).Must(x => x.EmployeeId is not null || x.PeriodId is not null).WithName("employeeId or periodId");
}

public sealed class ListIncentiveAssignmentsHandler(
    IPayrollPeriodRepository periods,
    IEmployeeIncentiveAssignmentRepository assignments,
    IIncentiveDefinitionRepository definitions,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant) : IQueryHandler<ListIncentiveAssignmentsQuery, IReadOnlyList<IncentiveAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<IncentiveAssignmentDto>>> Handle(ListIncentiveAssignmentsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmployeeIncentiveAssignment> list;
        if (query.PeriodId is { } periodId)
        {
            var period = await periods.FindByIdAsync(periodId, cancellationToken);
            if (period is null)
            {
                return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
            }

            list = await assignments.ListCoveringMonthAsync(EmployeeIncentiveAssignment.FirstOfMonth(period.Year, period.Month), cancellationToken);
            if (query.EmployeeId is { } onlyEmployee)
            {
                list = list.Where(a => a.EmployeeId == onlyEmployee).ToList();
            }
        }
        else
        {
            list = await assignments.ListByEmployeeAsync(query.EmployeeId!.Value, cancellationToken);
        }

        var defs = (await definitions.ListByIdsAsync(list.Select(a => a.IncentiveDefinitionId).Distinct().ToList(), cancellationToken)).ToDictionary(d => d.Id);
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, list.Select(a => a.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        IReadOnlyList<IncentiveAssignmentDto> dtos = list
            .Where(a => defs.ContainsKey(a.IncentiveDefinitionId))
            .Select(a =>
            {
                var definition = defs[a.IncentiveDefinitionId];
                var contact = contacts.GetValueOrDefault(a.EmployeeId);
                var name = contact is null ? a.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
                return new IncentiveAssignmentDto(
                    a.Id, a.EmployeeId, name, a.IncentiveDefinitionId, definition.Code, definition.Name, definition.DeclarationCode, definition.Kind,
                    definition.IsSmmmApproved, a.ValidFromMonth.Year, a.ValidFromMonth.Month, a.ValidToMonth?.Year, a.ValidToMonth?.Month, a.Note,
                    definition.IsActive, definition.ValidFrom, definition.ValidTo);
            }).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>
/// Assigns one incentive to 1..500 employees for a month range; all-or-nothing. Refused for closed periods, an inactive
/// definition, a same-category overlap, an SGK definition without declaration code and a MaxMonths violation.
/// An unapproved definition may be assigned (its code is declared, it just does not enter the calculation).
/// </summary>
[RequiresPermission("payroll.incentive.manage")]
public sealed record CreateIncentiveAssignmentsCommand(
    Guid IncentiveDefinitionId, IReadOnlyList<Guid> EmployeeIds, int FromYear, int FromMonth, int? ToYear, int? ToMonth, string? Note)
    : ICommand<CreateIncentiveAssignmentsResult>;

public sealed class CreateIncentiveAssignmentsValidator : AbstractValidator<CreateIncentiveAssignmentsCommand>
{
    public CreateIncentiveAssignmentsValidator()
    {
        RuleFor(x => x.IncentiveDefinitionId).NotEmpty();
        RuleFor(x => x.EmployeeIds).NotEmpty();
        RuleFor(x => x.EmployeeIds)
            .Must(ids => ids is null || ids.Distinct().Count() <= PayrollLimits.MaxPayItemAssignmentEmployees)
            .WithName("employeeIds");
        RuleFor(x => x.FromMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.FromYear).InclusiveBetween(1, 9999);
        RuleFor(x => x).Must(x => PayItemAssignmentInput.IsValidMonthPair(x.ToYear, x.ToMonth)).WithName("toYear/toMonth");
    }
}

public sealed class CreateIncentiveAssignmentsHandler(
    ITenantContext tenant,
    IPayrollPeriodRepository periods,
    IIncentiveDefinitionRepository definitions,
    IEmployeeIncentiveAssignmentRepository assignments,
    IEmployeeDirectory employeeDirectory,
    IncentiveChangeFlagger flagger) : ICommandHandler<CreateIncentiveAssignmentsCommand, CreateIncentiveAssignmentsResult>
{
    public async Task<Result<CreateIncentiveAssignmentsResult>> Handle(CreateIncentiveAssignmentsCommand command, CancellationToken cancellationToken)
    {
        var employeeIds = command.EmployeeIds.Distinct().ToList();
        var definition = await definitions.FindByIdAsync(command.IncentiveDefinitionId, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        var contacts = await employeeDirectory.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken);
        if (contacts.Select(c => c.EmployeeId).Distinct().Count() != employeeIds.Count)
        {
            return Error.NotFound(PayrollDomainErrors.PayItemEmployeeNotFound);
        }

        var from = EmployeeIncentiveAssignment.FirstOfMonth(command.FromYear, command.FromMonth);
        var to = PayItemAssignmentInput.ToMonth(command.ToYear, command.ToMonth);
        var closed = await ClosedMonths.LoadAsync(periods, cancellationToken);
        var existing = await assignments.ListByEmployeesAsync(employeeIds, cancellationToken);
        var byEmployee = existing.ToLookup(a => a.EmployeeId);
        var categoryOf = await IncentiveCategories.LoadAsync(definitions, existing, definition, cancellationToken);

        var created = new List<EmployeeIncentiveAssignment>();
        foreach (var employeeId in employeeIds)
        {
            var assignment = EmployeeIncentiveAssignment.Create(
                tenant.TenantId, employeeId, definition, from, to, command.Note, closed, byEmployee[employeeId].ToList(), categoryOf);
            if (assignment.IsFailure)
            {
                return assignment.Error;
            }

            created.Add(assignment.Value);
        }

        foreach (var assignment in created)
        {
            assignments.Add(assignment);
            await flagger.FlagEmployeeAsync(assignment.EmployeeId, assignment.ValidFromMonth, assignment.ValidToMonth, cancellationToken);
        }

        return new CreateIncentiveAssignmentsResult(created.Select(a => a.Id).ToList());
    }
}

/// <summary>Changes the end month and/or note. A used (closed-period) assignment can only be ended, not rewritten.</summary>
[RequiresPermission("payroll.incentive.manage")]
public sealed record UpdateIncentiveAssignmentCommand(Guid Id, int? ToYear, int? ToMonth, string? Note) : ICommand;

public sealed class UpdateIncentiveAssignmentValidator : AbstractValidator<UpdateIncentiveAssignmentCommand>
{
    public UpdateIncentiveAssignmentValidator() =>
        RuleFor(x => x).Must(x => PayItemAssignmentInput.IsValidMonthPair(x.ToYear, x.ToMonth)).WithName("toYear/toMonth");
}

public sealed class UpdateIncentiveAssignmentHandler(
    IPayrollPeriodRepository periods,
    IIncentiveDefinitionRepository definitions,
    IEmployeeIncentiveAssignmentRepository assignments,
    IncentiveChangeFlagger flagger) : ICommandHandler<UpdateIncentiveAssignmentCommand>
{
    public async Task<Result> Handle(UpdateIncentiveAssignmentCommand command, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindByIdAsync(command.Id, cancellationToken);
        if (assignment is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveAssignmentNotFound);
        }

        var definition = await definitions.FindByIdAsync(assignment.IncentiveDefinitionId, cancellationToken);
        if (definition is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveNotFound);
        }

        var oldTo = assignment.ValidToMonth;
        var newTo = PayItemAssignmentInput.ToMonth(command.ToYear, command.ToMonth);
        var closed = await ClosedMonths.LoadAsync(periods, cancellationToken);
        var sameEmployee = await assignments.ListByEmployeeAsync(assignment.EmployeeId, cancellationToken);
        var categoryOf = await IncentiveCategories.LoadAsync(definitions, sameEmployee, definition, cancellationToken);

        var updated = assignment.Update(definition, newTo, command.Note, closed, sameEmployee, categoryOf);
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
[RequiresPermission("payroll.incentive.manage")]
public sealed record DeleteIncentiveAssignmentCommand(Guid Id) : ICommand;

public sealed class DeleteIncentiveAssignmentHandler(
    IPayrollPeriodRepository periods,
    IEmployeeIncentiveAssignmentRepository assignments,
    IncentiveChangeFlagger flagger) : ICommandHandler<DeleteIncentiveAssignmentCommand>
{
    public async Task<Result> Handle(DeleteIncentiveAssignmentCommand command, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindByIdAsync(command.Id, cancellationToken);
        if (assignment is null)
        {
            return Error.NotFound(PayrollDomainErrors.IncentiveAssignmentNotFound);
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
