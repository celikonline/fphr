using Sense.Modules.Attendance.Contracts.Events;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Application.WorkSchedules;

/// <summary>
/// Shared by the "Yeni çalışma takvimi ekle" command path and <see cref="Employees.EmployeeHiredHandler"/>: lazily
/// seeds the tenant's default schedule ("Genel çalışma tablosu", mirrors Leave's EnsureDefaultRuleSetAsync) and
/// records a dated assignment, publishing WorkScheduleAssigned so Leave can drop its Monday–Friday placeholder.
/// </summary>
public sealed class WorkScheduleAssignmentService(
    IWorkScheduleRepository schedules, IEmployeeWorkScheduleAssignmentRepository assignments, IIntegrationEventOutbox outbox)
{
    public const string DefaultScheduleNameKey = "attendance.work_schedule.default.name";

    public async Task<WorkSchedule> EnsureDefaultScheduleAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = await schedules.FindDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var seeded = WorkSchedule.CreateDefault(tenantId, DefaultScheduleNameKey);
        schedules.Add(seeded);
        return seeded;
    }

    public async Task AssignAsync(
        Guid tenantId, Guid employeeId, Guid workScheduleId, DateOnly effectiveFrom, DateOnly? effectiveTo,
        WorkScheduleAssignmentSource source, Guid? assignedByUserId,
        CancellationToken cancellationToken)
    {
        var assignment = EmployeeWorkScheduleAssignment.Create(tenantId, employeeId, workScheduleId, effectiveFrom, effectiveTo, source, assignedByUserId);
        assignments.Add(assignment);
        outbox.Enqueue(new WorkScheduleAssigned(tenantId, employeeId, workScheduleId, effectiveFrom));
    }
}
