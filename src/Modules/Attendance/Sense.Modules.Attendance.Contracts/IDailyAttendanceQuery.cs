using Sense.Shared.Contracts.Paging;

namespace Sense.Modules.Attendance.Contracts;

/// <summary>Read port behind GET v1/attendance/daily (spec 5.4); data scope is applied by the implementation.</summary>
public interface IDailyAttendanceQuery
{
    Task<PagedResult<DailyAttendanceRowDto>> ListAsync(
        Guid tenantId, DateOnly date, DailyStatus status, Guid? orgUnitId, bool includeSubUnits, int page, int pageSize, CancellationToken ct);

    /// <summary>Per-status headcounts for the same filter (tab badges).</summary>
    Task<IReadOnlyDictionary<DailyStatus, int>> CountsAsync(
        Guid tenantId, DateOnly date, Guid? orgUnitId, bool includeSubUnits, CancellationToken ct);
}
