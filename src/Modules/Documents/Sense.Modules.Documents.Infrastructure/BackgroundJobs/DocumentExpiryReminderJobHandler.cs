using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Infrastructure.BackgroundJobs;

/// <summary>
/// ADR-0004: DI-discovered handler for <see cref="JobType.DocumentExpiryReminder"/>, registered in
/// DocumentsModule.AddModule (Hangfire runs in Sense.Api, not Sense.Worker - unlike the integration-event handlers
/// in DocumentsServiceCollectionExtensions.AddDocumentsIntegrationEventHandlers, which only Sense.Worker needs).
/// One recurring job per tenant is expected (BackgroundJobSeeding's per-tenant seeding model), so
/// <see cref="ScheduledJobContext.TenantId"/> should always be set here; a system-wide (null-tenant) invocation is a
/// no-op. Counts, not individual rows, are published - no Notifications consumer exists yet, see
/// DocumentExpiryReminderDue's remarks.
/// </summary>
public sealed class DocumentExpiryReminderJobHandler(
    IEmployeeDocumentRequirementRepository requirements,
    IIntegrationEventOutbox outbox,
    IDocumentsUnitOfWork unitOfWork,
    TimeProvider clock) : IScheduledJobHandler
{
    public JobType Handles => JobType.DocumentExpiryReminder;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        if (context.TenantId is not { } tenantId)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // Only the total counts are needed here (take: 1) - the FE-facing item list is GET /api/v1/documents/expiring.
        var (_, expiringTotal) = await requirements.ListExpiringAsync(
            today, DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expiring, 0, 1, ct);
        var (_, expiredTotal) = await requirements.ListExpiringAsync(
            today, DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expired, 0, 1, ct);

        if (expiringTotal == 0 && expiredTotal == 0)
        {
            return;
        }

        outbox.Enqueue(new DocumentExpiryReminderDue(tenantId, expiringTotal, expiredTotal));
        await unitOfWork.SaveChangesAsync(ct);
    }
}
