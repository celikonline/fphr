namespace Sense.Modules.Leave.Contracts;

/// <summary>One employee's approved leave on a given date; <see cref="LeaveTypeCode"/> is the leave type code, <see cref="IsPaid"/> its pay flag.</summary>
public sealed record EmployeeLeaveOnDate(Guid EmployeeId, string LeaveTypeCode, bool IsPaid);

/// <summary>
/// Who is on approved leave on a date (Attendance's "İzinde" list and absence rule). Implemented by Leave.Infrastructure;
/// <paramref name="employeeIds"/> null means every employee of the tenant.
/// </summary>
public interface ILeaveOnDateReader
{
    Task<IReadOnlyList<EmployeeLeaveOnDate>> ListAsync(
        Guid tenantId, DateOnly date, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
}
