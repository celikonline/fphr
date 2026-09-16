namespace Sense.Modules.Expense.Application.Payments;

/// <summary>
/// One row of the unified "Kendi hesabım > Ödemeler" self-service overview. Overtime/Adjustment kinds are part of
/// the design doc's IPaymentsOverviewQuery shape but not populated by this package (Leave's OvertimeConvertedToPayment
/// consumer and Payroll's PayrollAdjustment projection land in a later package) - the enum already reserves them so
/// the DTO shape does not need to change when they do.
/// </summary>
public enum PaymentKind
{
    Expense = 0,
    Advance = 1,
    Overtime = 2,
    Adjustment = 3,
}

public sealed record PaymentOverviewRow(PaymentKind Kind, DateOnly Date, decimal Amount, string Currency, string Status, Guid SourceId, string? Description);
