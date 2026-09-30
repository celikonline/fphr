namespace Sense.Modules.Attendance.Domain;

/// <summary>Domain error codes for the Attendance module (localized in SharedResource.resx).</summary>
public static class AttendanceDomainErrors
{
    public const string ScheduleNotFound = "attendance.work_schedule.not_found";
    public const string ScheduleNameRequired = "attendance.work_schedule.name_required";
    public const string ScheduleDaysCountInvalid = "attendance.work_schedule.days_count_invalid";
    public const string ScheduleDayRangeInvalid = "attendance.work_schedule.day_range_invalid";
    public const string ScheduleDayBreakInvalid = "attendance.work_schedule.day_break_invalid";
    public const string ScheduleIsDefault = "attendance.errors.schedule_is_default";
    public const string ScheduleHasEmployees = "attendance.errors.schedule_has_employees";
    public const string ScheduleInactive = "attendance.errors.schedule_inactive";
    public const string ScheduleDefaultCannotDeactivate = "attendance.errors.schedule_default_cannot_deactivate";
    public const string AssignmentEffectiveDateInPast = "attendance.assignment.effective_date_in_past";
    public const string AssignmentNotFound = "attendance.assignment.not_found";
    public const string AssignmentEndBeforeStart = "attendance.assignment.end_before_start";
    public const string AssignmentActiveAlreadyExists = "attendance.assignment.active_already_exists";
    public const string DayNotFound = "attendance.day.not_found";
    public const string DayCheckOutBeforeCheckIn = "attendance.day.check_out_before_check_in";
    public const string NoAnomalyToResolve = "attendance.day.no_anomaly_to_resolve";
    public const string DayAlreadyResolved = "attendance.day.already_resolved";
    public const string OverrideMinutesInvalid = "attendance.day.override_minutes_invalid";
    public const string AnomalyActionNotAllowed = "attendance.day.anomaly_action_not_allowed";
    public const string ShiftSettingsInvalid = "attendance.shift.settings_invalid";
    public const string ShiftTimeRangeInvalid = "attendance.shift.time_range_invalid";
    public const string ShiftAlreadyPublished = "attendance.shift.already_published";
    public const string ShiftNotFound = "attendance.shift.not_found";
    public const string ShiftPeriodLocked = "attendance.shift.period_locked";
    public const string ShiftPeriodAlreadyLocked = "attendance.shift.period_already_locked";
    public const string ShiftPeriodNotLocked = "attendance.shift.period_not_locked";
    public const string ShiftUnlockReasonRequired = "attendance.shift.unlock_reason_required";
    public const string ShiftDailyLimitExceeded = "attendance.shift.daily_limit_exceeded";
    public const string ShiftWeeklyHoursExceeded = "attendance.shift.weekly_hours_exceeded";
    public const string ShiftWeeklyDaysExceeded = "attendance.shift.weekly_days_exceeded";
    public const string ShiftAvailabilityConflict = "attendance.shift.availability_conflict";
    public const string ShiftRestPeriodTooShort = "attendance.shift.rest_period_too_short";
    public const string ShiftBreakTooShort = "attendance.shift.break_too_short";
    public const string AvailabilityTimeRangeInvalid = "attendance.availability.time_range_invalid";
    public const string AvailabilityAlreadyCancelled = "attendance.availability.already_cancelled";
    public const string AvailabilityNotFound = "attendance.availability.not_found";
    public const string AvailabilityNotOwner = "attendance.availability.not_owner";
    public const string CompensatoryLedgerNotFound = "attendance.compensatory.not_found";
    public const string CompensatoryLedgerNotOpen = "attendance.compensatory.not_open";
    public const string CompensatoryMinutesInvalid = "attendance.compensatory.minutes_invalid";
    public const string EqualizationPeriodMonthsInvalid = "attendance.equalization.period_months_invalid";
    public const string EqualizationWrittenAgreementRequired = "attendance.equalization.written_agreement_required";
    public const string EqualizationAnchorMonthInvalid = "attendance.equalization.anchor_month_invalid";
    public const string EqualizationPeriodNotOpen = "attendance.equalization.period_not_open";
    public const string EqualizationPeriodNotYetEnded = "attendance.equalization.period_not_yet_ended";
    public const string EqualizationMinutesInvalid = "attendance.equalization.minutes_invalid";

    // zaman-devam W0-a (11.10): okutma, vardiya tanımı, eksik kayıt talebi, şablon, rotasyon, terminal.
    public const string PunchInFuture = "attendance.punch.in_future";
    public const string PunchEmployeeRequired = "attendance.punch.employee_required";
    public const string PunchWorkDateInvalid = "attendance.punch.work_date_invalid";
    public const string PunchCoordinatesInvalid = "attendance.punch.coordinates_invalid";
    public const string PunchReasonTooLong = "attendance.punch.reason_too_long";
    public const string PunchDeleteReasonRequired = "attendance.punch.delete_reason_required";
    public const string PunchAlreadyDeleted = "attendance.punch.already_deleted";
    public const string ShiftDefinitionNotFound = "attendance.shift_definition.not_found";
    public const string ShiftDefinitionCodeRequired = "attendance.shift_definition.code_required";
    public const string ShiftDefinitionNameRequired = "attendance.shift_definition.name_required";
    public const string ShiftDefinitionColorInvalid = "attendance.shift_definition.color_invalid";
    public const string ShiftDefinitionTimeRangeInvalid = "attendance.shift_definition.time_range_invalid";
    public const string ShiftDefinitionFlexibleWindowInvalid = "attendance.shift_definition.flexible_window_invalid";
    public const string ShiftDefinitionToleranceInvalid = "attendance.shift_definition.tolerance_invalid";
    public const string ShiftDefinitionOvertimeRangeInvalid = "attendance.shift_definition.overtime_range_invalid";
    public const string ShiftDefinitionBreakInvalid = "attendance.shift_definition.break_invalid";
    public const string ShiftDayTypeInvalid = "attendance.shift.day_type_invalid";
    public const string ShiftDefinitionRequired = "attendance.shift.definition_required";
    public const string PunchRequestNotFound = "attendance.punch_request.not_found";
    public const string PunchRequestReasonRequired = "attendance.punch_request.reason_required";
    public const string PunchRequestDirectionInvalid = "attendance.punch_request.direction_invalid";
    public const string PunchRequestNotPending = "attendance.punch_request.not_pending";
    public const string ShiftTemplateNotFound = "attendance.shift_template.not_found";
    public const string ShiftTemplateNameRequired = "attendance.shift_template.name_required";
    public const string ShiftTemplatePeriodInvalid = "attendance.shift_template.period_invalid";
    public const string ShiftTemplateEntryInvalid = "attendance.shift_template.entry_invalid";
    public const string RotationPatternNotFound = "attendance.rotation.not_found";
    public const string RotationPatternCodeRequired = "attendance.rotation.code_required";
    public const string RotationPatternNameRequired = "attendance.rotation.name_required";
    public const string RotationCycleInvalid = "attendance.rotation.cycle_invalid";
    public const string RotationStepInvalid = "attendance.rotation.step_invalid";
    public const string RotationAssignmentNotFound = "attendance.rotation_assignment.not_found";
    public const string RotationAssignmentRangeInvalid = "attendance.rotation_assignment.range_invalid";
    public const string TerminalNotFound = "attendance.terminal.not_found";
    public const string TerminalCodeRequired = "attendance.terminal.code_required";
    public const string TerminalNameRequired = "attendance.terminal.name_required";
    public const string TerminalApiKeyRequired = "attendance.terminal.api_key_required";
    public const string TerminalGroupNotFound = "attendance.terminal_group.not_found";
    public const string TerminalGroupNameRequired = "attendance.terminal_group.name_required";
    public const string TerminalCredentialValueRequired = "attendance.terminal_credential.value_required";
    public const string TerminalCredentialNotFound = "attendance.terminal_credential.not_found";
    public const string TerminalCredentialInUse = "attendance.terminal_credential.in_use";
    public const string CorrectionTimesRequired = "attendance.correction.times_required";
    public const string CorrectionInvalidRange = "attendance.correction.invalid_range";
    public const string CorrectionReasonRequired = "attendance.correction.reason_required";
    public const string CorrectionNotFound = "attendance.correction.not_found";
    public const string CorrectionNotPending = "attendance.correction.not_pending";
}

/// <summary>Domain limits.</summary>
public static class AttendanceLimits
{
    public const int NameMaxLength = 200;
    public const int DaysPerWeek = 7;

    /// <summary>A calendar day has 24 hours: no single day can carry more worked minutes than this.</summary>
    public const int MaxMinutesPerDay = 24 * 60;
}
