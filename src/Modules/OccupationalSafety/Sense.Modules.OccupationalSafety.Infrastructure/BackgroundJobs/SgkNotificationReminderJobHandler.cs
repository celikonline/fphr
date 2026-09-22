using Sense.Modules.OccupationalSafety.Application;
using Sense.Modules.OccupationalSafety.Contracts.Events;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.OccupationalSafety.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily per-tenant reminder (<see cref="JobType.OhsSgkNotificationReminder"/>, registered in OhsModule - Hangfire
/// runs in Sense.Api): counts work accidents not yet notified to SGK whose deadline is today/tomorrow and those past
/// it, and publishes <see cref="SgkNotificationReminderDue"/> for Notifications. Same shape as Documents'
/// DocumentExpiryReminderJobHandler; nothing is published when there is nothing to remind about.
/// </summary>
public sealed class SgkNotificationReminderJobHandler(
    IOhsIncidentRepository incidents,
    IIntegrationEventOutbox outbox,
    IOhsUnitOfWork unitOfWork,
    TimeProvider clock) : IScheduledJobHandler
{
    public JobType Handles => JobType.OhsSgkNotificationReminder;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        if (context.TenantId is not { } tenantId)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var awaiting = await incidents.ListAwaitingSgkNotificationAsync(ct);
        var overdue = awaiting.Count(i => i.SgkNotificationDeadline < today);
        var dueSoon = awaiting.Count(i => i.SgkNotificationDeadline >= today && i.SgkNotificationDeadline <= today.AddDays(1));
        if (overdue == 0 && dueSoon == 0)
        {
            return;
        }

        outbox.Enqueue(new SgkNotificationReminderDue(tenantId, dueSoon, overdue));
        await unitOfWork.SaveChangesAsync(ct);
    }
}
