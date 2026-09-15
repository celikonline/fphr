namespace Sense.Modules.Payroll.Domain;

/// <summary>Domain error codes for the Payroll module (localized in SharedResource.resx).</summary>
public static class PayrollDomainErrors
{
    public const string PeriodNotFound = "payroll.period.not_found";
    public const string PeriodNotOpen = "payroll.period.not_open";
    public const string PeriodMonthInvalid = "payroll.errors.period_month_invalid";
    public const string PayslipNotFound = "payroll.payslip.not_found";
    public const string GrossAmountInvalid = "payroll.errors.gross_amount_invalid";
    public const string WorkedDaysInvalid = "payroll.errors.worked_days_invalid";
    public const string MealVariantNotSupported = "payroll.errors.meal_variant_not_supported";
}

/// <summary>Domain limits.</summary>
public static class PayrollLimits
{
    /// <summary>Calendar days assumed per month until Attendance provides the real calendar (roadmap: Payroll depends on Attendance, Faz 2).</summary>
    public const int DefaultCalendarDaysInMonth = 30;
}
