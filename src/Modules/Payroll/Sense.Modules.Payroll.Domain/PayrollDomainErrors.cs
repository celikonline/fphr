namespace Sense.Modules.Payroll.Domain;

/// <summary>Domain error codes for the Payroll module (localized in SharedResource.resx).</summary>
public static class PayrollDomainErrors
{
    public const string PeriodNotFound = "payroll.period.not_found";
    public const string PeriodNotOpen = "payroll.period.not_open";
    public const string PeriodMonthInvalid = "payroll.errors.period_month_invalid";
    public const string BankFilePeriodNotApproved = "payroll.errors.bank_file_period_not_approved";
    public const string BankFileMissingAccounts = "payroll.errors.bank_file_missing_accounts";
    public const string BankFileNoPayments = "payroll.errors.bank_file_no_payments";
    public const string BankFileTotalMismatch = "payroll.errors.bank_file_total_mismatch";
    public const string ComparisonThresholdInvalid = "payroll.errors.comparison_threshold_invalid";
    public const string ClosingCheckNotFound = "payroll.errors.closing_check_not_found";
    public const string ClosingCheckNotManual = "payroll.errors.closing_check_not_manual";
    public const string ClosingWaiverReasonInvalid = "payroll.errors.closing_waiver_reason_invalid";
    public const string PeriodHasOpenBlockers = "payroll.errors.period_has_open_blockers";
    public const string PeriodHasDirtyPayslips = "payroll.errors.period_has_dirty_payslips";
    public const string PeriodAlreadyOpen = "payroll.errors.period_already_open";
    public const string PeriodYearOutOfRange = "payroll.errors.period_year_out_of_range";

    /// <summary>A statutory LegislationParameter has no value for the requested period/date (422, not a 500).</summary>
    public const string LegislationParameterMissing = "payroll.errors.legislation_parameter_missing";

    public const string SeveranceTerminationDateInvalid = "payroll.errors.severance_termination_date_invalid";
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

    /// <summary>senseik-011 (F-06): the severance/notice estimate needs a current salary record (Employee.Contracts.IEmployeeSalaryLookup) to derive a daily wage from.</summary>
    public const string SeveranceEstimateSalaryMissing = "payroll.errors.severance_estimate_salary_missing";

    /// <summary>Çıkış hesabı: no frozen settlement exists for the employee (not terminated, or terminated before this feature).</summary>
    public const string ExitSettlementNotFound = "payroll.errors.exit_settlement_not_found";
    public const string PeriodNotReopenable = "payroll.errors.period_not_reopenable";
    public const string PeriodReopenReasonInvalid = "payroll.errors.period_reopen_reason_invalid";
    public const string ExitSettlementLineInvalid = "payroll.errors.exit_settlement_line_invalid";
    public const string ExitSettlementLineNotFound = "payroll.errors.exit_settlement_line_not_found";
    public const string ExitSettlementLineNotRemovable = "payroll.errors.exit_settlement_line_not_removable";

    // İcra/nafaka kesinti emirleri (senseik-019 2c)
    public const string GarnishmentCaseReferenceRequired = "payroll.errors.garnishment_case_reference_required";
    public const string GarnishmentInstallmentInvalid = "payroll.errors.garnishment_installment_invalid";
    public const string GarnishmentBalanceInvalid = "payroll.errors.garnishment_balance_invalid";
    public const string GarnishmentAlreadyClosed = "payroll.errors.garnishment_already_closed";
    public const string GarnishmentDateRangeInvalid = "payroll.errors.garnishment_date_range_invalid";
    public const string GarnishmentNotFound = "payroll.garnishment.not_found";

    // Sendika (union) üyeliği ve aidat kesintisi
    public const string UnionNotFound = "payroll.union.not_found";
    public const string UnionNameRequired = "payroll.errors.union_name_required";
    public const string UnionNameTooLong = "payroll.errors.union_name_too_long";
    public const string UnionMembershipNotFound = "payroll.union_membership.not_found";
    public const string UnionMembershipUnionRequired = "payroll.errors.union_membership_union_required";
    public const string UnionMembershipDateRangeInvalid = "payroll.errors.union_membership_date_range_invalid";
    public const string UnionMembershipDuesValueInvalid = "payroll.errors.union_membership_dues_value_invalid";
    public const string UnionMembershipAlreadyOpen = "payroll.errors.union_membership_already_open";
}

/// <summary>Domain limits.</summary>
public static class PayrollLimits
{
    public const int ExitLineLabelMaxLength = 200;
    public const int ReopenReasonMaxLength = 500;
    public const int ClosingCheckTextMaxLength = 500;

    /// <summary>Calendar days assumed per month until Attendance provides the real calendar (roadmap: Payroll depends on Attendance, Faz 2).</summary>
    public const int DefaultCalendarDaysInMonth = 30;

    /// <summary>Earliest payroll period year that can be opened (first year the product is used).</summary>
    public const int MinPeriodYear = 2024;

    /// <summary>A hypothetical severance exit date may be at most this many days ahead of today.</summary>
    public const int MaxSeveranceTerminationDaysAhead = 366;

    public const int ExternalPayrollCodeMaxLength = 50;
    public const int NotesMaxLength = 2000;
    public const int IncentiveLawCodeMaxLength = 30;
    public const int UnionNameMaxLength = 200;
    public const int UnionIndustryBranchCodeMaxLength = 30;
}
