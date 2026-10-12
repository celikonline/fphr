namespace Sense.Modules.Payroll.Domain;

/// <summary>Domain error codes for the Payroll module (localized in SharedResource.resx).</summary>
public static class PayrollDomainErrors
{
    public const string PeriodNotFound = "payroll.period.not_found";
    public const string PeriodNotOpen = "payroll.period.not_open";
    public const string PeriodMonthInvalid = "payroll.errors.period_month_invalid";
    public const string ProfileTransportAmountInvalid = "payroll.errors.profile_transport_amount_invalid";
    public const string AdjustmentInvalid = "payroll.errors.adjustment_invalid";
    public const string AdjustmentNotFound = "payroll.errors.adjustment_not_found";
    public const string AdjustmentPeriodNotOpen = "payroll.errors.adjustment_period_not_open";
    public const string BankFilePeriodNotApproved = "payroll.errors.bank_file_period_not_approved";
    public const string BankFileMissingAccounts = "payroll.errors.bank_file_missing_accounts";
    public const string BankFileNoPayments = "payroll.errors.bank_file_no_payments";
    public const string BankFileTotalMismatch = "payroll.errors.bank_file_total_mismatch";
    public const string ReceiptCsvEmpty = "payroll.errors.receipt_csv_empty";
    public const string ComparisonThresholdInvalid = "payroll.errors.comparison_threshold_invalid";
    public const string ClosingCheckNotFound = "payroll.errors.closing_check_not_found";
    public const string ClosingCheckNotManual = "payroll.errors.closing_check_not_manual";
    public const string ClosingWaiverReasonInvalid = "payroll.errors.closing_waiver_reason_invalid";
    public const string PeriodHasOpenBlockers = "payroll.errors.period_has_open_blockers";
    public const string PeriodHasDirtyPayslips = "payroll.errors.period_has_dirty_payslips";
    public const string PeriodApprovalNeedsDifferentUser = "payroll.errors.period_approval_needs_different_user";
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

    // Bordro onay/itiraz (E2, K4)
    public const string PayslipNotPublished = "payroll.errors.payslip_not_published";
    public const string PayslipAlreadyAcknowledged = "payroll.errors.payslip_already_acknowledged";
    public const string PayslipObjectionPending = "payroll.errors.payslip_objection_pending";
    public const string PayslipObjectionAlreadyFiled = "payroll.errors.payslip_objection_already_filed";
    public const string PayslipObjectionReasonInvalid = "payroll.errors.payslip_objection_reason_invalid";
    public const string PayslipObjectionNotPending = "payroll.errors.payslip_objection_not_pending";

    /// <summary>Another request changed the payslip first (optimistic concurrency, xmin): e.g. two acknowledgements or acknowledge vs objection.</summary>
    public const string PayslipConcurrentUpdate = "payroll.errors.payslip_concurrent_update";

    /// <summary>The signed-in user has no linked employee record, so there are no "my" payslips to act on.</summary>
    public const string NoEmployeeForUser = "payroll.errors.no_employee_for_user";

    // Pay-item formula language (positions are 1-based; Error.Args: position, name, max).
    public const string FormulaEmpty = "payroll.formula.empty";
    public const string FormulaSyntax = "payroll.formula.syntax";
    public const string FormulaUnknownVariable = "payroll.formula.unknown_variable";
    public const string FormulaUnknownFunction = "payroll.formula.unknown_function";
    public const string FormulaArgumentCount = "payroll.formula.argument_count";
    public const string FormulaTooLong = "payroll.formula.too_long";
    public const string FormulaTooDeep = "payroll.formula.too_deep";
    public const string FormulaDivisionByZero = "payroll.formula.division_by_zero";
    public const string FormulaOverflow = "payroll.formula.overflow";
    public const string FormulaDecimalComma = "payroll.formula.decimal_comma";

    // PAY-FORMULA-2: pay item definitions and assignments.
    public const string PayItemCodeInvalid = "payroll.errors.pay_item_code_invalid";
    public const string PayItemCodeTaken = "payroll.errors.pay_item_code_taken";
    public const string PayItemNameInvalid = "payroll.errors.pay_item_name_invalid";
    public const string PayItemDirectionNotSupported = "payroll.errors.pay_item_direction_not_supported";
    public const string PayItemNetRequiresFullyTaxable = "payroll.errors.pay_item_net_requires_fully_taxable";
    public const string PayItemDateRangeInvalid = "payroll.errors.pay_item_date_range_invalid";
    public const string PayItemNotFound = "payroll.errors.pay_item_not_found";
    public const string PayItemInactive = "payroll.errors.pay_item_inactive";
    public const string PayItemAssignmentNotFound = "payroll.errors.pay_item_assignment_not_found";
    public const string PayItemAssignmentPeriodClosed = "payroll.errors.pay_item_assignment_period_closed";
    public const string PayItemAssignmentTooMany = "payroll.errors.pay_item_assignment_too_many";
    public const string PayItemAssignmentValueRequired = "payroll.errors.pay_item_assignment_value_required";
    public const string PayItemAssignmentOverlap = "payroll.errors.pay_item_assignment_overlap";

    /// <summary>Reuses the shared Employee.Contracts "employee.not_found" message for an unknown/other-tenant employee id.</summary>
    public const string PayItemEmployeeNotFound = "employee.not_found";
}

/// <summary>Domain limits.</summary>
public static class PayrollLimits
{
    public const int ExitLineLabelMaxLength = 200;
    public const int ReopenReasonMaxLength = 500;
    public const int ClosingCheckTextMaxLength = 500;
    public const int AdjustmentLabelMaxLength = 200;

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

    /// <summary>PAY-FORMULA: at most this many pay items may apply to one employee in one month.</summary>
    public const int MaxPayItemsPerEmployeeMonth = 50;

    /// <summary>PAY-FORMULA: employees per bulk pay-item assignment request.</summary>
    public const int MaxPayItemAssignmentEmployees = 500;

    /// <summary>E2: an employee's payslip objection text.</summary>
    public const int ObjectionReasonMaxLength = 1000;

    /// <summary>K4 evidence: client IP (IPv6 with zone fits) and User-Agent kept with an acknowledgement.</summary>
    public const int ClientIpMaxLength = 64;

    public const int UserAgentMaxLength = 512;

    /// <summary>E2: the daily reminder only chases payslips published within this many days (no endless reminders for old periods).</summary>
    public const int AcknowledgementReminderWindowDays = 90;
}
