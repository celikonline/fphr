using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Infrastructure.Persistence;

public sealed class WorkScheduleRepository(AttendanceDbContext context) : IWorkScheduleRepository
{
    public void Add(WorkSchedule schedule) => context.WorkSchedules.Add(schedule);

    public void Remove(WorkSchedule schedule) => context.WorkSchedules.Remove(schedule);

    public async Task<Result<WorkSchedule>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await context.WorkSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return schedule is null ? Error.NotFound(AttendanceDomainErrors.ScheduleNotFound) : schedule;
    }

    public async Task<WorkSchedule?> FindDefaultAsync(CancellationToken cancellationToken = default) =>
        await context.WorkSchedules.FirstOrDefaultAsync(s => s.IsDefault, cancellationToken)
        ?? context.WorkSchedules.Local.FirstOrDefault(s => s.IsDefault);

    public async Task<IReadOnlyList<WorkSchedule>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.WorkSchedules.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query.OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public Task<int> CountAssignedEmployeesAsync(Guid workScheduleId, CancellationToken cancellationToken = default)
    {
        // Current assignment per employee = the row whose EffectiveFrom is the employee's max (GroupBy+First doesn't
        // translate to SQL; GroupBy+Max does, so the "current row" is found by joining back on the max date).
        var maxEffectiveFromByEmployee = context.WorkScheduleAssignments
            .GroupBy(a => a.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, EffectiveFrom = g.Max(a => a.EffectiveFrom) });

        var current = context.WorkScheduleAssignments.Join(
            maxEffectiveFromByEmployee,
            a => new { a.EmployeeId, a.EffectiveFrom },
            m => new { m.EmployeeId, m.EffectiveFrom },
            (a, _) => a);

        return current.CountAsync(a => a.WorkScheduleId == workScheduleId, cancellationToken);
    }
}

public sealed class EmployeeWorkScheduleAssignmentRepository(AttendanceDbContext context) : IEmployeeWorkScheduleAssignmentRepository
{
    public void Add(EmployeeWorkScheduleAssignment assignment) => context.WorkScheduleAssignments.Add(assignment);

    public async Task<EmployeeWorkScheduleAssignment?> FindEffectiveOnAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        await context.WorkScheduleAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveFrom <= date)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.WorkScheduleAssignments
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeWorkScheduleAssignment>> ListCoveringRangeAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var all = await context.WorkScheduleAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveFrom <= to)
            .OrderBy(a => a.EffectiveFrom)
            .ToListAsync(cancellationToken);

        var lastBeforeRange = all.LastOrDefault(a => a.EffectiveFrom <= from);
        return all.Where(a => a.EffectiveFrom >= from || a == lastBeforeRange).ToList();
    }
}
