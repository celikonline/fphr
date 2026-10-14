using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Daily;

/// <summary>GET v1/attendance/daily (spec 5.4).</summary>
[RequiresPermission(AttendancePermissions.DailyViewKey)]
public sealed record ListDailyAttendanceQuery(DateOnly Date, DailyStatus? Status, Guid? OrgUnitId, bool IncludeSubUnits, int Page, int PageSize)
    : IQuery<PagedResult<DailyAttendanceRowDto>>;

public sealed class ListDailyAttendanceHandler(ITenantContext tenant, IDailyAttendanceQuery query)
    : IQueryHandler<ListDailyAttendanceQuery, PagedResult<DailyAttendanceRowDto>>
{
    public async Task<Result<PagedResult<DailyAttendanceRowDto>>> Handle(ListDailyAttendanceQuery request, CancellationToken cancellationToken) =>
        await query.ListAsync(tenant.TenantId, request.Date, request.Status, request.OrgUnitId, request.IncludeSubUnits, request.Page, request.PageSize, cancellationToken);
}

/// <summary>GET v1/attendance/daily/counts: camelCase status name to count; every status is present (0 when empty).</summary>
[RequiresPermission(AttendancePermissions.DailyViewKey)]
public sealed record GetDailyAttendanceCountsQuery(DateOnly Date, Guid? OrgUnitId, bool IncludeSubUnits) : IQuery<IReadOnlyDictionary<string, int>>;

public sealed class GetDailyAttendanceCountsHandler(ITenantContext tenant, IDailyAttendanceQuery query)
    : IQueryHandler<GetDailyAttendanceCountsQuery, IReadOnlyDictionary<string, int>>
{
    public async Task<Result<IReadOnlyDictionary<string, int>>> Handle(GetDailyAttendanceCountsQuery request, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyDictionary<string, int>>(
            (await query.CountsAsync(tenant.TenantId, request.Date, request.OrgUnitId, request.IncludeSubUnits, cancellationToken))
            .ToDictionary(kv => char.ToLowerInvariant(kv.Key.ToString()[0]) + kv.Key.ToString()[1..], kv => kv.Value));
}
