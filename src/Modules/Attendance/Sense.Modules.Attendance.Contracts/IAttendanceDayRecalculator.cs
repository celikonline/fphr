namespace Sense.Modules.Attendance.Contracts;

/// <summary>Rebuilds AttendanceDay rows from punches + resolved plans; locked days are skipped and resolution state is kept.</summary>
public interface IAttendanceDayRecalculator
{
    /// <summary>Queues a background recalculation for the range (all employees when <paramref name="employeeIds"/> is null); returns the job id.</summary>
    Task<Guid> EnqueueAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);

    /// <summary>Progress of a queued job; null when the job is unknown for the tenant.</summary>
    Task<RecalculationStatusDto?> GetStatusAsync(Guid tenantId, Guid jobId, CancellationToken ct);

    /// <summary>Synchronous recalculation for small ranges; returns the number of days rebuilt.</summary>
    Task<int> RecalculateNowAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
}
