using Sense.Modules.Attendance.Domain.AttendanceDays;
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
