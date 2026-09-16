using Microsoft.Extensions.Logging;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Domain;
using Sense.Shared.Contracts.BackgroundJobs;

namespace Sense.Modules.Recruitment.Infrastructure.BackgroundJobs;

/// <summary>
/// ADR-0004: DI-discovered handler for <see cref="JobType.CandidateRetentionSweep"/> (Faz 9 "KVKK uyumlu aday
/// verisi saklama ve imha"), registered in RecruitmentModule.AddModule (Hangfire runs in Sense.Api, mirrors
/// Documents' DocumentExpiryReminderJobHandler). Runs the same sweep as the manual
/// <see cref="Sense.Modules.Recruitment.Application.Candidates.AnonymizeExpiredCandidatesCommand"/> (HR/ops
/// trigger), but the job handler cannot go through the CQRS dispatcher: that command carries
/// <c>[RequiresPermission]</c> and <see cref="ScheduledJobContext"/> has no authenticated user, so
/// <c>AuthorizationBehaviour</c> would always reject it. Duplicates the handler's small query+loop instead, exactly
/// like <c>DocumentExpiryReminderJobHandler</c> calls its repository directly rather than dispatching a command.
/// One recurring job per tenant is expected (BackgroundJobSeeding's per-tenant seeding model - not wired yet, see
/// rapor); a system-wide (null-tenant) invocation is a no-op.
/// </summary>
public sealed class CandidateRetentionSweepJobHandler(
    ICandidateRepository candidates,
    IRecruitmentUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CandidateRetentionSweepJobHandler> logger) : IScheduledJobHandler
{
    public JobType Handles => JobType.CandidateRetentionSweep;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        if (context.TenantId is not { } tenantId)
        {
            return;
        }

        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMonths(-RecruitmentLimits.CandidateDataRetentionMonths);
        var expired = await candidates.ListRetentionExpiredAsync(cutoff, ct);

        var anonymizedCount = 0;
        foreach (var candidate in expired)
        {
            if (candidate.Anonymize().IsSuccess)
            {
                anonymizedCount++;
            }
        }

        if (anonymizedCount == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        // KVKK denetim izi: kim (sistem/job), ne (imha), ne zaman (context.TriggeredAt).
        logger.LogInformation(
            "KVKK saklama süresi dolan aday imhası: kiracıId={TenantId}, imhaEdilenAdaySayısı={AnonymizedCount}, " +
            "tetiklenmeZamanı={TriggeredAt}",
            tenantId, anonymizedCount, context.TriggeredAt);
    }
}
