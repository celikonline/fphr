using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Adjustments;

/// <summary>What an extra taxable earning of one payroll month is.</summary>
public enum PayrollAdjustmentKind
{
    Bonus = 0,
    Commission = 1,
    Performance = 2,
    Other = 3,
}

/// <summary>
/// A one-off gross earning added to one employee's payroll for one month (prim, komisyon, performans ödemesi, ikramiye). It is
/// taxable like the salary: it raises the gross and with it SGK, gelir vergisi and damga vergisi, so the net rises by the after-tax part.
/// Own aggregate because it carries the cross-module <see cref="EmployeeId"/> (same pattern as <c>GarnishmentOrder</c>).
/// </summary>
public sealed class PayrollAdjustment : TenantAggregateRoot<Guid>
{
    private PayrollAdjustment()
    {
    }

    private PayrollAdjustment(Guid id, Guid tenantId, Guid employeeId, int year, int month, PayrollAdjustmentKind kind, string label, decimal grossAmount, bool isNet)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Year = year;
        Month = month;
        Kind = kind;
        Label = label;
        GrossAmount = grossAmount;
        IsNet = isNet;
    }

    public Guid EmployeeId { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public PayrollAdjustmentKind Kind { get; private set; }

    public string Label { get; private set; } = string.Empty;

    /// <summary>The entered amount: gross added to the month's taxable earnings, or - when <see cref="IsNet"/> - the net the employee must keep (grossed up at calculation).</summary>
    public decimal GrossAmount { get; private set; }

    /// <summary>True when <see cref="GrossAmount"/> was agreed as a net amount (net prim); the payroll grosses it up.</summary>
    public bool IsNet { get; private set; }

    public static Result<PayrollAdjustment> Create(Guid tenantId, Guid employeeId, int year, int month, PayrollAdjustmentKind kind, string label, decimal grossAmount, bool isNet = false)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);
        if (month is < 1 or > 12)
        {
            return Error.Validation(PayrollDomainErrors.PeriodMonthInvalid);
        }

        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > PayrollLimits.AdjustmentLabelMaxLength || grossAmount <= 0m)
        {
            return Error.Validation(PayrollDomainErrors.AdjustmentInvalid);
        }

        return new PayrollAdjustment(Guid.NewGuid(), tenantId, employeeId, year, month, kind, label.Trim(), grossAmount, isNet);
    }
}
