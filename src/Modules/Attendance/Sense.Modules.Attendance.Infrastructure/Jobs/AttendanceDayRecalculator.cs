using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Context;

namespace Sense.Modules.Attendance.Infrastructure.Jobs;

/// <summary>
/// <see cref="IAttendanceDayRecalculator"/> (zaman-devam Z0.3): singleton; jobs live in memory (<see cref="RecalculationJobStore"/>)
/// and are executed one at a time by <see cref="RecalculationHostedService"/>. Every run gets its own DI scope and tenant scope.
/// A restart loses queued/in-flight jobs - callers re-queue, the run is idempotent.
/// The queue is drained only where <see cref="RecalculationHostedService"/> runs (API; marked by <see cref="RecalculationQueueConsumer"/>):
/// elsewhere (Sense.Worker) <see cref="EnqueueAsync"/> refuses instead of leaving a job queued forever - use <see cref="RecalculateNowAsync"/>.
/// </summary>
public sealed partial class AttendanceDayRecalculator(
    IServiceScopeFactory scopes, RecalculationJobStore store, TimeProvider clock, ILogger<AttendanceDayRecalculator> logger,
    RecalculationQueueConsumer? consumer = null) : IAttendanceDayRecalculator
{
    /// <summary>
    /// The enqueuing command (e.g. "Kaydet &amp; Geçmişe Uygula") may not have committed yet when it queues the job; waiting before the
    /// first read lets its transaction finish so the fresh scope sees the new definitions.
    /// </summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(3);

    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Queue => _queue.Reader;

    public async Task<Guid> EnqueueAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct)
    {
        if (consumer is null)
        {
            LogNoQueueConsumer(logger, tenantId);
            throw new InvalidOperationException(
                "Attendance recalculation queue has no consumer in this host (RecalculationHostedService runs in the API only); use RecalculateNowAsync.");
        }

        var job = store.Create(tenantId, from, to, employeeIds);
        await _queue.Writer.WriteAsync(job.JobId, ct).ConfigureAwait(false);
        return job.JobId;
    }

    public Task<RecalculationStatusDto?> GetStatusAsync(Guid tenantId, Guid jobId, CancellationToken ct) =>
        Task.FromResult(store.Find(tenantId, jobId)?.ToDto());

    public Task<int> RecalculateNowAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct) =>
        ExecuteAsync(tenantId, from, to, employeeIds, null, ct);

    /// <summary>Runs a queued job to completion; failures are recorded on the job, never thrown.</summary>
    public async Task RunJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = store.FindById(jobId);
        if (job is null)
        {
            return;
        }

        try
        {
            await Task.Delay(StartDelay, clock, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        store.Update(jobId, j => j with { State = RecalculationJob.Running });
        try
        {
            await ExecuteAsync(
                job.TenantId, job.From, job.To, job.EmployeeIds,
                (total, done) => store.Update(jobId, j => j with { Total = total, Done = done }), ct).ConfigureAwait(false);
            store.Update(jobId, j => j with { State = RecalculationJob.Completed });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogJobFailed(logger, ex, jobId);
            store.Update(jobId, j => j with { State = RecalculationJob.Failed, Error = ex.Message });
        }
    }

    private async Task<int> ExecuteAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, Action<int, int>? progress, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().BeginScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<RecalculationExecutor>()
            .RunAsync(tenantId, from, to, employeeIds, progress, ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 7410, Level = LogLevel.Error, Message = "Attendance recalculation job {JobId} failed")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 7411, Level = LogLevel.Error, Message = "Attendance recalculation enqueued for tenant {TenantId} in a host without a queue consumer")]
    private static partial void LogNoQueueConsumer(ILogger logger, Guid tenantId);
}

/// <summary>Marker: this host drains the recalculation queue (<see cref="RecalculationHostedService"/> registered alongside it).</summary>
public sealed class RecalculationQueueConsumer;

/// <summary>Drains the recalculation queue in the API process.</summary>
public sealed class RecalculationHostedService(AttendanceDayRecalculator recalculator) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in recalculator.Queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await recalculator.RunJobAsync(jobId, stoppingToken).ConfigureAwait(false);
        }
    }
}
