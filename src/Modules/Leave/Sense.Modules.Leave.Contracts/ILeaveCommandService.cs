using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// Cross-module write port other modules (Attendance's anomaly resolution, package A3) use to create leave/overtime
/// records on an employee's behalf. Implemented by Sense.Modules.Leave.Infrastructure via IDispatcher, so the
/// command pipeline (UnitOfWork, validation, permission check) applies exactly as it does for Leave's own
/// İşlemler → İzin Ekle / Fazla mesai ekle screens - the caller must hold the same leave.requests.create_on_behalf /
/// leave.requests.bypass_approval / leave.overtimes.create_on_behalf permissions those screens require. Leave only
/// enforces its own process integrity, not who may act on an employee's behalf from Attendance.
/// </summary>
public interface ILeaveCommandService
{
    /// <summary>
    /// "İzin talebi" resolution (11.3.6): a single-day leave request added for <paramref name="employeeId"/>,
    /// approved at once without the Workflow approval process (same mechanism as İşlemler → İzin Ekle's
    /// "Onay sürecine dahil edilmeden onaylansın"). Returns the created LeaveRequest's id.
    /// </summary>
    Task<Result<Guid>> CreateApprovedLeaveAsync(
        Guid employeeId, Guid leaveTypeId, DateOnly startDate, DateOnly endDate, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Fazla mesai ekle" resolution (11.3.6): opens an overtime request (Source=Admin) for <paramref name="employeeId"/>;
    /// still runs the normal Workflow approval (11.3.06 open point #3 - no bypass-approval path exists for overtime
    /// yet). Returns the created OvertimeRequest's id.
    /// </summary>
    Task<Result<Guid>> CreateOvertimeRequestAsync(
        Guid employeeId, DateOnly workDate, TimeOnly startTime, int durationMinutes, string? description, CancellationToken cancellationToken = default);
}
