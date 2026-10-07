using System.Globalization;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Modules.Reporting.Infrastructure.Persistence;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Modules.Reporting.Infrastructure.Jobs;

public sealed class ReportGenerationJob(
    ReportingDbContext context, IEnumerable<IReportProvider> providers, IPermissionService permissions,
    ReportGenerationContext generation, ITenantContext tenant, TimeProvider clock,
    ILogger<ReportGenerationJob> logger, IIntegrationEventOutbox outbox) : IScheduledJobHandler
{
    public JobType Handles => JobType.ExportReport;

    public async Task ExecuteAsync(ScheduledJobContext job, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<ReportGenerationRequested>(job.PayloadJson ?? "{}");
        if (request is null || job.TenantId != request.TenantId || tenant.TenantId != request.TenantId || request.RequestedByUserId == Guid.Empty)
        {
            logger.LogWarning("Invalid report job context");
            return;
        }

        // A session advisory lock survives the separate durable Running/terminal saves.
        // Concurrent delivery returns immediately; after a process crash the session releases the lock for retry.
        var lockKey = BitConverter.ToInt64(SHA256.HashData(request.ExecutionId.ToByteArray()));
        await context.Database.OpenConnectionAsync(ct);
        var locked = false;
        try
        {
            locked = await AdvisoryLockAsync(lockKey, acquire: true, ct);
            if (!locked)
            {
                return;
            }

        var execution = await context.ReportExecutions.SingleOrDefaultAsync(e => e.Id == request.ExecutionId && e.TenantId == request.TenantId, ct);
        if (execution is null || execution.RequestedByUserId != request.RequestedByUserId || execution.Status is ReportExecutionStatus.Succeeded or ReportExecutionStatus.Failed)
        {
            return;
        }

        using var userScope = CurrentUserAccessor.Use(new AuthenticatedUser(request.RequestedByUserId, request.EmployeeId,
            request.Email, request.DisplayName, new HashSet<string>(), false, null, null, null, null));
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(request.CultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            generation.Set(request);
            var provider = providers.FirstOrDefault(p => p.Key == execution.ReportKey && p.ExecutionMode == "async");
            if (provider is null || !await permissions.HasAsync(request.RequestedByUserId, "reports.executions.generate", ct)
                || provider.RequiredPermission is { } required && !await permissions.HasAsync(request.RequestedByUserId, required, ct))
            {
                execution.Fail(ReportingErrors.ReportPermissionDenied, clock.GetUtcNow().UtcDateTime);
            }
            else
            {
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(execution.ParametersJson ?? "{}") ?? [];
                execution.Start(clock.GetUtcNow().UtcDateTime);
                await context.SaveChangesAsync(ct);
                var file = await provider.GenerateAsync(request.TenantId, parameters, ct);
                execution.Complete(file.FileName, file.ContentType, file.Content, clock.GetUtcNow().UtcDateTime);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report generation failed for execution {ExecutionId}", execution.Id);
            execution.Fail(ReportingErrors.GenerationFailed, clock.GetUtcNow().UtcDateTime);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        // Persist the terminal result and ready-email outbox event atomically. Mail retries are independent of
        // generation, and a duplicate job delivery sees Succeeded and cannot enqueue a second event.
        if (execution.Status == ReportExecutionStatus.Succeeded && request.Notify && request.Email is { Length: > 0 } email)
        {
            outbox.Enqueue(new ReportReadyNotificationRequested(request.TenantId, execution.Id, request.RequestedByUserId,
                email, request.DisplayName, execution.ReportKey, request.CultureName));
        }
        await context.SaveChangesAsync(ct);
        }
        finally
        {
            if (locked)
            {
                await AdvisoryLockAsync(lockKey, acquire: false, CancellationToken.None);
            }
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> AdvisoryLockAsync(long key, bool acquire, CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = acquire ? "SELECT pg_try_advisory_lock(@key)" : "SELECT pg_advisory_unlock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.DbType = DbType.Int64;
        parameter.Value = key;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
}
