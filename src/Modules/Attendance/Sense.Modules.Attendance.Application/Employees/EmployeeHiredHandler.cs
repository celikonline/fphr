using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Application.Employees;

/// <summary>
/// EmployeeHired → default work schedule assignment (11.3.6 "EmployeeHired → varsayılan takvim ataması,
/// EffectiveFrom=HireDate, Source=Hire"). Idempotent: an outbox redelivery finds the existing assignment and does nothing.
/// </summary>
public sealed class EmployeeHiredHandler(
    IEmployeeWorkScheduleAssignmentRepository assignments, WorkScheduleAssignmentService assignmentService, IAttendanceUnitOfWork unitOfWork)
    : IIntegrationEventHandler<EmployeeHired>
{
    public async Task Handle(EmployeeHired integrationEvent, CancellationToken cancellationToken)
    {
        if (await assignments.FindEffectiveOnAsync(integrationEvent.EmployeeId, integrationEvent.HireDate, cancellationToken) is not null)
        {
            return;
        }

        var schedule = await assignmentService.EnsureDefaultScheduleAsync(integrationEvent.TenantId, cancellationToken);
        await assignmentService.AssignAsync(
            integrationEvent.TenantId, integrationEvent.EmployeeId, schedule.Id, integrationEvent.HireDate,
            null, WorkScheduleAssignmentSource.Hire, null, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
