using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Adjustments;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Adjustments;

/// <summary>One-off taxable earning (prim, komisyon...) of a payroll month, with the employee's name.</summary>
public sealed record PayrollAdjustmentDto(Guid Id, Guid EmployeeId, string EmployeeName, PayrollAdjustmentKind Kind, string Label, decimal GrossAmount, bool IsNet);

/// <summary>The period's extra earnings ("Ek kazançlar"), all employees.</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ListPayrollAdjustmentsQuery(Guid PeriodId) : IQuery<IReadOnlyList<PayrollAdjustmentDto>>;

public sealed class ListPayrollAdjustmentsHandler(
    IPayrollPeriodRepository periods, IPayrollAdjustmentRepository adjustments, IEmployeeDirectory employeeDirectory, ITenantContext tenant)
    : IQueryHandler<ListPayrollAdjustmentsQuery, IReadOnlyList<PayrollAdjustmentDto>>
{
    public async Task<Result<IReadOnlyList<PayrollAdjustmentDto>>> Handle(ListPayrollAdjustmentsQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var list = await adjustments.ListByMonthAsync(period.Year, period.Month, cancellationToken);
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, list.Select(a => a.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);
        IReadOnlyList<PayrollAdjustmentDto> dtos = list.Select(a =>
        {
            var contact = contacts.GetValueOrDefault(a.EmployeeId);
            var name = contact is null ? a.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
            return new PayrollAdjustmentDto(a.Id, a.EmployeeId, name, a.Kind, a.Label, a.GrossAmount, a.IsNet);
        }).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Adds a gross one-off earning to an employee's payroll in an <b>open</b> period and marks that payslip changed so the next run picks it up.</summary>
[RequiresPermission("payroll.adjustment.manage")]
public sealed record AddPayrollAdjustmentCommand(Guid PeriodId, Guid EmployeeId, PayrollAdjustmentKind Kind, string Label, decimal GrossAmount, bool IsNet = false) : ICommand<Guid>;

public sealed class AddPayrollAdjustmentValidator : AbstractValidator<AddPayrollAdjustmentCommand>
{
    public AddPayrollAdjustmentValidator()
    {
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(PayrollLimits.AdjustmentLabelMaxLength);
        RuleFor(x => x.GrossAmount).GreaterThan(0);
    }
}

public sealed class AddPayrollAdjustmentHandler(
    IPayrollPeriodRepository periods, IPayrollAdjustmentRepository adjustments, PayslipDirtyFlagService flagging, ITenantContext tenant)
    : ICommandHandler<AddPayrollAdjustmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddPayrollAdjustmentCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.AdjustmentPeriodNotOpen);
        }

        var created = PayrollAdjustment.Create(tenant.TenantId, command.EmployeeId, period.Year, period.Month, command.Kind, command.Label, command.GrossAmount, command.IsNet);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        adjustments.Add(created.Value);
        await flagging.FlagAsync(
            tenant.TenantId, command.EmployeeId, new DateOnly(period.Year, period.Month, 1), PayslipDirtyFlagReasons.AdjustmentChanged, null, cancellationToken);
        return Result.Success(created.Value.Id);
    }
}

/// <summary>Removes an earning added by mistake; only while its period is still open. The payslip is marked changed.</summary>
[RequiresPermission("payroll.adjustment.manage")]
public sealed record RemovePayrollAdjustmentCommand(Guid AdjustmentId) : ICommand;

public sealed class RemovePayrollAdjustmentHandler(
    IPayrollPeriodRepository periods, IPayrollAdjustmentRepository adjustments, PayslipDirtyFlagService flagging, ITenantContext tenant)
    : ICommandHandler<RemovePayrollAdjustmentCommand>
{
    public async Task<Result> Handle(RemovePayrollAdjustmentCommand command, CancellationToken cancellationToken)
    {
        var adjustment = await adjustments.FindByIdAsync(command.AdjustmentId, cancellationToken);
        if (adjustment is null)
        {
            return Error.NotFound(PayrollDomainErrors.AdjustmentNotFound);
        }

        var period = await periods.FindByYearMonthAsync(adjustment.Year, adjustment.Month, cancellationToken);
        if (period is null || period.Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.AdjustmentPeriodNotOpen);
        }

        adjustments.Remove(adjustment);
        await flagging.FlagAsync(
            tenant.TenantId, adjustment.EmployeeId, new DateOnly(adjustment.Year, adjustment.Month, 1), PayslipDirtyFlagReasons.AdjustmentChanged, null, cancellationToken);
        return Result.Success();
    }
}
