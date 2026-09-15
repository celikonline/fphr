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
}

/// <summary>Domain limits.</summary>
public static class LeaveLimits
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int ReasonMaxLength = 1000;
    public const int MaxDays = 365;
    public const int MaxWaitingPeriodMonths = 24;
    public const int IdempotencyKeyMaxLength = 200;
}
