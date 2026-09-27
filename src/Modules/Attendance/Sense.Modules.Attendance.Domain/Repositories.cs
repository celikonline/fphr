using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Modules.Attendance.Domain.Compensatory;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain;

public interface IWorkScheduleRepository
{
    void Add(WorkSchedule schedule);

    void Remove(WorkSchedule schedule);

    Task<Result<WorkSchedule>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkSchedule?> FindDefaultAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkSchedule>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task<int> CountAssignedEmployeesAsync(Guid workScheduleId, CancellationToken cancellationToken = default);
}

public interface IEmployeeWorkScheduleAssignmentRepository
{
    void Add(EmployeeWorkScheduleAssignment assignment);

    void Remove(EmployeeWorkScheduleAssignment assignment);

    Task<Result<EmployeeWorkScheduleAssignment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The assignment in effect on <paramref name="date"/> (latest EffectiveFrom on or before it), or null if none.</summary>
    Task<EmployeeWorkScheduleAssignment?> FindEffectiveOnAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Every assignment of the employee that could be effective at some point in [from, to] (the one just before <paramref name="from"/> included).</summary>
    Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListCoveringRangeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IAttendanceDayRepository
{
    void Add(AttendanceDay day);

    Task<Result<AttendanceDay>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AttendanceDay?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceDay>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Ekip haftalık ızgarası (11.3.6): every recorded day in [from, to] across a set of employees, in one query.</summary>
    Task<IReadOnlyList<AttendanceDay>> ListForEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IAnomalyResolutionRepository
{
    void Add(AnomalyResolution resolution);

    Task<IReadOnlyList<AnomalyResolution>> ListForDayAsync(Guid attendanceDayId, CancellationToken cancellationToken = default);
}

public interface IShiftSettingsRepository
{
    void Add(ShiftSettings settings);

    Task<ShiftSettings?> FindAsync(CancellationToken cancellationToken = default);
}

public interface IShiftAssignmentRepository
{
    void Add(ShiftAssignment assignment);

    Task<Result<ShiftAssignment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ShiftAssignment?> FindByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Haftalık/aylık görünüm (11.3.6): every assignment in [from, to], optionally scoped to a set of employees.</summary>
    Task<IReadOnlyList<ShiftAssignment>> ListForRangeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);

    /// <summary>"Bekleyen/paylaşılmamış vardiyalar" sayacı (11.3.6 aylık görünüm).</summary>
    Task<int> CountUnpublishedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IShiftPlanLockRepository
{
    void Add(ShiftPlanLock plan);

    Task<ShiftPlanLock?> FindByPeriodAsync(DateOnly periodMonth, CancellationToken cancellationToken = default);
}

public interface IEmployeeAvailabilityRepository
{
    void Add(EmployeeAvailability availability);

    Task<Result<EmployeeAvailability>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeAvailability>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IMonthlyTimesheetApprovalRepository
{
    void Add(MonthlyTimesheetApproval approval);

    Task<MonthlyTimesheetApproval?> FindAsync(Guid employeeId, DateOnly periodMonth, CancellationToken cancellationToken = default);

    /// <summary>Aylık puantaj ızgarası (S-11): every existing approval row for these employees in this period, in one query.</summary>
    Task<IReadOnlyList<MonthlyTimesheetApproval>> ListForPeriodAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly periodMonth, CancellationToken cancellationToken = default);
}

/// <summary>Write-once audit log (S-31); no update/remove - see <see cref="SelfCheckIn"/>.</summary>
public interface ISelfCheckInRepository
{
    void Add(SelfCheckIn checkIn);

    /// <summary>The employee's latest <see cref="PunchDirection.BreakStart"/>/<see cref="PunchDirection.BreakEnd"/> row at or after <paramref name="sinceUtc"/>; null when there is none.</summary>
    Task<SelfCheckIn?> FindLatestBreakPunchAsync(Guid employeeId, DateTime sinceUtc, CancellationToken cancellationToken = default);
}

public interface ICompensatoryWorkLedgerRepository
{
    void Add(CompensatoryWorkLedger ledger);

    Task<Result<CompensatoryWorkLedger>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CompensatoryWorkLedger?> FindByEmployeeAndWeekAsync(Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompensatoryWorkLedger>> ListForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IEqualizationPeriodRepository
{
    void Add(EqualizationPeriod period);

    /// <summary>The period row whose [PeriodStart, PeriodEnd] contains <paramref name="date"/>, if one has already been opened.</summary>
    Task<EqualizationPeriod?> FindCoveringAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Closed periods whose PeriodEnd falls inside [from, to] (Payroll bridge: attributes the true-up to the month a period closes in).</summary>
    Task<IReadOnlyList<EqualizationPeriod>> ListClosedEndingInRangeAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
