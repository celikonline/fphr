using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Modules.Employee.Api;

/// <summary>
/// Runs <see cref="TerminationReasonCorrectionService"/> for every writable tenant (senseik-006/F-01 backfill, handoff §5 risk 2).
/// Tenants whose termination reason catalog was seeded before the SGK exit code table was verified still carry the old
/// wrong system codes (e.g. Resignation "29" while only "03" is allowed), which makes an İstifa termination impossible to
/// complete. Same startup pattern as Identity's <c>SystemRolePermissionSynchronizer</c>: idempotent, per-tenant failure
/// isolated, and only system rows still holding the exact old-known-wrong value are touched (tenant edits survive).
/// </summary>
public sealed partial class TerminationReasonCorrectionSynchronizer(
    IActiveTenantDirectory tenants,
    ITenantContextSetter tenantSetter,
    TerminationReasonCorrectionService correction,
    IEmployeeUnitOfWork unitOfWork,
    ILogger<TerminationReasonCorrectionSynchronizer> logger)
{
    /// <returns>Total number of reason rows corrected across all tenants.</returns>
    public async Task<int> SyncAllTenantsAsync(CancellationToken ct)
    {
        var tenantIds = await tenants.GetActiveTenantIdsAsync(ct).ConfigureAwait(false);
        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                total += await SyncTenantAsync(tenantId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, tenantId, ex);
            }
        }

        return total;
    }

    public async Task<int> SyncTenantAsync(Guid tenantId, CancellationToken ct)
    {
        using var scope = tenantSetter.BeginScope(tenantId);

        var corrected = await correction.CorrectKnownWrongSystemCodesAsync(ct).ConfigureAwait(false);
        if (corrected > 0)
        {
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            LogTenantCorrected(logger, tenantId, corrected);
        }

        return corrected;
    }

    [LoggerMessage(EventId = 2120, Level = LogLevel.Information, Message = "Termination reason SGK exit codes corrected for tenant {TenantId}: {Corrected} row(s)")]
    private static partial void LogTenantCorrected(ILogger logger, Guid tenantId, int corrected);

    [LoggerMessage(EventId = 2121, Level = LogLevel.Error, Message = "Termination reason SGK exit code correction failed for tenant {TenantId}; continuing with other tenants")]
    private static partial void LogTenantFailed(ILogger logger, Guid tenantId, Exception exception);
}

/// <summary>API startup: runs the correction once; a failure (e.g. database not migrated yet) never blocks startup and is retried on the next start.</summary>
public sealed partial class TerminationReasonCorrectionHostedService(IServiceScopeFactory scopes, ILogger<TerminationReasonCorrectionHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var system = CurrentUserAccessor.UseSystem();
            await using var scope = scopes.CreateAsyncScope();
            var synchronizer = scope.ServiceProvider.GetRequiredService<TerminationReasonCorrectionSynchronizer>();
            await synchronizer.SyncAllTenantsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSyncFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 2122, Level = LogLevel.Warning, Message = "Termination reason SGK exit code correction failed; it will be retried on next start")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);
}
