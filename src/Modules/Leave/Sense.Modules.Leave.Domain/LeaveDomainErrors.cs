namespace Sense.Modules.Leave.Domain;

/// <summary>Domain error codes for the Leave module (localized in SharedResource.resx).</summary>
public static class LeaveDomainErrors
{
    public const string RuleSetNotFound = "leave.rule_set.not_found";
    public const string RuleSetNameRequired = "leave.rule_set.name_required";
    public const string TypeNotFound = "leave.type.not_found";
    public const string TypeNameRequired = "leave.type.name_required";
    public const string TypeCodeDuplicate = "leave.type.code_duplicate";
    public const string TypePerRequestMaxDaysInvalid = "leave.type.per_request_max_days_invalid";
    public const string TypeAnnualDaysInvalid = "leave.type.annual_days_invalid";
    public const string TypeNegativeBalanceLimitInvalid = "leave.type.negative_balance_limit_invalid";
    public const string TypeCarryOverLimitInvalid = "leave.type.carry_over_limit_invalid";
    public const string TypeConsecutiveDaysInvalid = "leave.type.consecutive_days_invalid";
    public const string TypeWaitingPeriodInvalid = "leave.type.waiting_period_invalid";
    public const string TypeSeniorityTiersOnlyAnnual = "leave.type.seniority_tiers_only_annual";
    public const string LedgerAmountZero = "leave.ledger.amount_zero";
    public const string LedgerReasonRequired = "leave.ledger.reason_required";
    public const string ProfileNotFound = "leave.profile.not_found";
    public const string RequestNotFound = "leave.request.not_found";
    public const string RequestNotPending = "leave.request.not_pending";
    public const string RequestApprovalMismatch = "leave.request.approval_mismatch";
    public const string RequestInvalidRange = "leave.errors.invalid_range";
    public const string RequestZeroDays = "leave.errors.zero_days";
    public const string RequestPartialDayRequiresSingleDate = "leave.errors.partial_day_requires_single_date";
    public const string RequestPartialDayTimeInvalid = "leave.errors.partial_day_time_invalid";
    public const string RequestNotCancellable = "leave.errors.request_not_cancellable";
    public const string RequestCancellationReasonRequired = "leave.errors.cancellation_reason_required";
    public const string RequestInvalidAttachment = "leave.errors.invalid_attachment";
    public const string RuleSetIsDefault = "leave.errors.rule_set_is_default";
    public const string RuleSetHasEmployees = "leave.errors.rule_set_has_employees";
    public const string OvertimeNotFound = "leave.overtime.not_found";
    public const string OvertimeNotPending = "leave.overtime.not_pending";
    public const string OvertimeInvalidDuration = "leave.errors.overtime_invalid_duration";
    public const string OvertimeInvalidOverlap = "leave.errors.overtime_invalid_overlap";
    public const string OvertimeInvalidPayRateKind = "leave.errors.overtime_invalid_pay_rate_kind";
    public const string OvertimeNotApproved = "leave.errors.overtime_not_approved";
    public const string OvertimeAlreadyConverted = "leave.errors.overtime_already_converted";
    public const string OvertimeInvalidRate = "leave.errors.overtime_invalid_rate";
    public const string OvertimeNothingToConvert = "leave.errors.overtime_nothing_to_convert";
    public const string OvertimeConsentNotFound = "leave.overtime_consent.not_found";
    public const string OvertimeConsentAlreadyRevoked = "leave.overtime_consent.already_revoked";
    public const string OvertimeConsentInvalidRevocation = "leave.overtime_consent.invalid_revocation";
    public const string AbsenceNoteTooLong = "leave.absence.note_too_long";
    public const string AbsenceHoursInvalid = "leave.absence.hours_invalid";
    public const string AbsenceHoursOnlyForLate = "leave.absence.hours_only_for_late";
    public const string AbsenceHoursRequiredForLate = "leave.absence.hours_required_for_late";
    public const string AbsenceReversalReasonRequired = "leave.absence.reversal_reason_required";
    public const string AbsenceAlreadyReversed = "leave.absence.already_reversed";
    public const string AbsenceReversalNotReversible = "leave.absence.reversal_not_reversible";
}

/// <summary>Domain limits.</summary>
public static class LeaveLimits
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int ReasonMaxLength = 1000;
    public const int CancellationReasonMaxLength = 500;
    public const int CalendarMaxRangeDays = 93;

    /// <summary>Longest span the request preview endpoints accept (a year plus slack for split/overlapping periods).</summary>
    public const int PreviewMaxRangeDays = 400;
    public const int MaxDays = 365;
    public const int MaxWaitingPeriodMonths = 24;
    public const int IdempotencyKeyMaxLength = 200;
    public const int AbsenceNoteMaxLength = 500;

    /// <summary>Ekli dosya adı anlık görüntüsü (işletim sistemi yol sınırı altında).</summary>
    public const int AttachmentFileNameMaxLength = 260;
}
