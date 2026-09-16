namespace Sense.Modules.Payroll.Domain;

/// <summary>Domain error codes for the Payroll module (localized in SharedResource.resx).</summary>
public static class PayrollDomainErrors
{
    public const string PeriodNotFound = "payroll.period.not_found";
    public const string PeriodNotOpen = "payroll.period.not_open";
    public const string PeriodMonthInvalid = "payroll.errors.period_month_invalid";
    public const string PeriodAlreadyOpen = "payroll.errors.period_already_open";
    public const string PeriodNotApproved = "payroll.errors.period_not_approved";
    public const string PeriodNotPaid = "payroll.errors.period_not_paid";
    public const string PayslipNotFound = "payroll.payslip.not_found";
    public const string GrossAmountInvalid = "payroll.errors.gross_amount_invalid";
    public const string WorkedDaysInvalid = "payroll.errors.worked_days_invalid";
    public const string MealVariantNotSupported = "payroll.errors.meal_variant_not_supported";
    public const string ProfileNotFound = "payroll.profile.not_found";
    public const string ProfileDateRangeInvalid = "payroll.errors.profile_date_range_invalid";
    public const string ProfileSgkWorkplaceRequired = "payroll.errors.profile_sgk_workplace_required";
    public const string ProfileMealAmountInvalid = "payroll.errors.profile_meal_amount_invalid";
    public const string ProfileOverlap = "payroll.errors.profile_overlap";
    public const string ProfileExternalCodeTooLong = "payroll.errors.profile_external_code_too_long";
    public const string ProfileNotesTooLong = "payroll.errors.profile_notes_too_long";
}

/// <summary>Domain limits.</summary>
public static class PayrollLimits
{
    /// <summary>Calendar days assumed per month until Attendance provides the real calendar (roadmap: Payroll depends on Attendance, Faz 2).</summary>
    public const int DefaultCalendarDaysInMonth = 30;

    public const int ExternalPayrollCodeMaxLength = 50;
    public const int NotesMaxLength = 2000;
    public const int IncentiveLawCodeMaxLength = 30;
}
