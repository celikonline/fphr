using System.Collections.Concurrent;
using Sense.Modules.Attendance.Contracts;

namespace Sense.Modules.Attendance.Application.Recalculation;

/// <summary>One queued/running recalculation (in memory; progress is lost on restart - the run is idempotent, so it is simply re-queued).</summary>
public sealed record RecalculationJob(
    Guid JobId, Guid TenantId, DateOnly From, DateOnly To, IReadOnlyCollection<Guid>? EmployeeIds,
    string State = RecalculationJob.Queued, int Total = 0, int Done = 0, string? Error = null)
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public RecalculationStatusDto ToDto() => new(JobId, State, Total, Done, Error);
}

/// <summary>Singleton in-memory job registry (no table by decision: ledger ruling for Z0.3).</summary>
public sealed class RecalculationJobStore
{
    private const int MaxRetained = 200;

    private readonly ConcurrentDictionary<Guid, RecalculationJob> _jobs = new();

    public RecalculationJob Create(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds)
    {
        if (_jobs.Count >= MaxRetained)
        {
            foreach (var old in _jobs.Values.Where(j => j.State is RecalculationJob.Completed or RecalculationJob.Failed).Take(MaxRetained / 2))
            {
                _jobs.TryRemove(old.JobId, out _);
            }
        }

        var job = new RecalculationJob(Guid.CreateVersion7(), tenantId, from, to, employeeIds);
        _jobs[job.JobId] = job;
        return job;
    }

    public RecalculationJob? Find(Guid tenantId, Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) && job.TenantId == tenantId ? job : null;

    /// <summary>Worker-side lookup (the queue carries only the id); API-side reads must use the tenant-checked <see cref="Find"/>.</summary>
    public RecalculationJob? FindById(Guid jobId) => _jobs.TryGetValue(jobId, out var job) ? job : null;

    public void Update(Guid jobId, Func<RecalculationJob, RecalculationJob> change)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = change(job);
        }
    }
}
