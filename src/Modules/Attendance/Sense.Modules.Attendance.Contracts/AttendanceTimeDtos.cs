namespace Sense.Modules.Attendance.Contracts;

/// <summary>Direction of a punch (okutma) as reported by its source; <see cref="Unknown"/> when the device/user gave none.</summary>
public enum PunchDirectionDto { In, Out, Unknown }

/// <summary>Where a punch came from; serialized camelCase by the API's string enum converter.</summary>
public enum PunchSourceDto { Manual, Import, Device, SelfService, Request, Terminal }

/// <summary>Daily list buckets of "Günlük Puantaj" (spec 5.4); each value maps to one rule of IDailyAttendanceQuery.</summary>
public enum DailyStatus { Incalculable, Arrived, Late, EarlyLeave, Absent, Overtime, Undertime, OnLeave }

/// <summary>One punch to record. <see cref="SourceRef"/> is the source's own idempotency reference (device log id, request id, ...).</summary>
public sealed record PunchInput(
    Guid TenantId, Guid EmployeeId, DateTime OccurredAtUtc, PunchDirectionDto Direction,
    PunchSourceDto Source, string? SourceRef, Guid? TerminalId, Guid? LocationId, double? Latitude, double? Longitude,
    bool IsManual, Guid? CreatedByUserId, string? Reason);

/// <summary>Outcome of a batch: newly stored punches vs. duplicates skipped by the idempotency rule.</summary>
public sealed record PunchBatchResult(int Accepted, int Ignored);

/// <summary>Progress of a queued recalculation job; <see cref="State"/> is the job's lifecycle name (queued/running/completed/failed).</summary>
public sealed record RecalculationStatusDto(Guid JobId, string State, int Total, int Done, string? Error);

/// <summary>One row of a daily attendance list.</summary>
public sealed record DailyAttendanceRowDto(
    Guid EmployeeId, string EmployeeName, string? OrgUnitName, DateOnly Date,
    TimeOnly? CheckIn, TimeOnly? CheckOut, int WorkedMinutes, int OvertimeMinutes, string[] Anomalies, string? LeaveType);

/// <summary>One day's headcounts for the dashboard trend (planned employees, arrived, late, early leave, absent, on leave).</summary>
public sealed record DailyAttendanceTotalsDto(DateOnly Date, int Planned, int Arrived, int Late, int EarlyLeave, int Absent, int OnLeave);

/// <summary>One employee's period totals; Analytics groups them by unit/branch/team/title (Attendance does not know the org).</summary>
public sealed record EmployeeAttendancePeriodDto(
    Guid EmployeeId, int LateCount, int LateMinutes, int EarlyLeaveCount, int EarlyLeaveMinutes,
    int OvertimeMinutes, int PlannedShiftDays, int WorkedShiftDays);
