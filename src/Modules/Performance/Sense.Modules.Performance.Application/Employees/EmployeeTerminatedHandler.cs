using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Performance.Domain;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Performance.Application.Employees;

/// <summary>
/// EmployeeTerminated -> archive (freeze, don't delete/cancel) every open/in-progress goal owned by the terminated
/// employee. Decision (rapor'da gerekçe): Archived, deleted/cancelled değil - denetim izini (KVKK + 4857 md.18 fesih
/// delili) korur; Completed/Cancelled goals are left untouched. Idempotent: an outbox redelivery finds no more open
/// goals and does nothing (Goal.Archive is itself a no-op on an already-terminal goal too).
/// </summary>
public sealed class EmployeeTerminatedHandler(IGoalRepository goals, IPerformanceUnitOfWork unitOfWork) : IIntegrationEventHandler<EmployeeTerminated>
{
    public async Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken)
    {
        var openGoals = await goals.ListOpenByEmployeeAsync(integrationEvent.EmployeeId, cancellationToken);
        if (openGoals.Count == 0)
        {
            return;
        }

        foreach (var goal in openGoals)
        {
            goal.Archive();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
