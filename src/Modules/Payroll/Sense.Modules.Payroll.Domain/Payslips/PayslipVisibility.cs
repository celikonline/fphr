namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>
/// TK-030: which of an employee's own payslips they may see/download. An Open or Approved period can still change
/// before payment, so only <see cref="PayrollPeriodStatus.Paid"/> and <see cref="PayrollPeriodStatus.Locked"/>
/// periods are visible to the employee themself (HR/Payroll still see every period via payroll.trace.view).
/// </summary>
public static class PayslipVisibility
{
    public static bool IsVisibleToEmployee(PayrollPeriodStatus status) =>
        status is PayrollPeriodStatus.Paid or PayrollPeriodStatus.Locked;
}
