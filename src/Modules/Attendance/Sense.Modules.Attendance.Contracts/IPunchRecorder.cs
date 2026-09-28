using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Contracts;

/// <summary>
/// The single write path for punches (okutma): manual entry, PDKS import, devices, self-service, approved missing-punch
/// requests and terminals all go through it, and each call triggers the affected day's recalculation.
/// </summary>
public interface IPunchRecorder
{
    /// <summary>Records one punch and recalculates its day; returns the punch id.</summary>
    Task<Result<Guid>> RecordAsync(PunchInput input, CancellationToken ct);

    /// <summary>Idempotent bulk record: duplicates (same uniqueness key) are counted as ignored, not errors.</summary>
    Task<Result<PunchBatchResult>> RecordBatchAsync(IReadOnlyList<PunchInput> inputs, CancellationToken ct);

    /// <summary>Soft-deletes a punch (reason mandatory, period-lock and audit rules apply) and recalculates its day.</summary>
    Task<Result> SoftDeleteAsync(Guid tenantId, Guid punchId, Guid userId, string reason, CancellationToken ct);
}
