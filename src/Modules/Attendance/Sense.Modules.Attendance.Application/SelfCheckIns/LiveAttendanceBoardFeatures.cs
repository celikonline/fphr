using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.SelfCheckIns;

/// <summary>One row of the canlı içeride/dışarıda panosu (S-31 kabul kriteri 3): today's check-in/out for one visible employee.</summary>
public sealed record LiveAttendanceStatusDto(
    Guid EmployeeId, string EmployeeName, TimeOnly? CheckIn, TimeOnly? CheckOut, bool IsInside, AttendanceDaySource? Source);

/// <summary>
/// Canlı içeride/dışarıda panosu (S-31 kabul kriteri 3): o an kim işte kim değil, günün AttendanceDay kayıtlarından
/// türetilir - self-servis QR/GPS check-in, PDKS cihaz/Excel içe alma ve manuel kayıt hepsi aynı satırda görünür
/// (kabul kriteri 4: hepsi aynı ortak upsert'ten geçiyor). Polling ile yenilenir (WebSocket zorunlu değil).
/// </summary>
[RequiresPermission(AttendancePermissions.BoardViewKey)]
public sealed record GetLiveAttendanceBoardQuery(Guid? OrgUnitId) : IQuery<IReadOnlyList<LiveAttendanceStatusDto>>;

public sealed class GetLiveAttendanceBoardHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory employees, IAttendanceDayRepository days, ITenantLocalClock clock)
    : IQueryHandler<GetLiveAttendanceBoardQuery, IReadOnlyList<LiveAttendanceStatusDto>>
{
    public async Task<Result<IReadOnlyList<LiveAttendanceStatusDto>>> Handle(GetLiveAttendanceBoardQuery query, CancellationToken cancellationToken)
    {
        var employeeIds = await VisibleEmployeeResolver.ResolveAsync(
            AttendancePermissions.BoardViewKey, query.OrgUnitId, tenant.TenantId, dataScope, employees, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<LiveAttendanceStatusDto>>([]);
        }

        // Tenant-local "today" (not UTC).
        var today = DateOnly.FromDateTime((await clock.NowAsync(tenant.TenantId, cancellationToken)).Local);
        return Result.Success(await LiveAttendanceBoard.BuildAsync(employeeIds, today, tenant.TenantId, employees, days, cancellationToken));
    }
}

/// <summary>Today's board rows for an already resolved employee set (the data-scope board above, or FG-007's direct reports).</summary>
internal static class LiveAttendanceBoard
{
    public static async Task<IReadOnlyList<LiveAttendanceStatusDto>> BuildAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly today, Guid tenantId, IEmployeeDirectory employees, IAttendanceDayRepository days,
        CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        // Yesterday is read too: a night-shift employee whose evening "in" is still open (no "out" yet) is inside this
        // morning, and only has a record dated yesterday.
        var yesterday = today.AddDays(-1);
        var recent = await days.ListForEmployeesAsync(employeeIds, yesterday, today, cancellationToken);
        var records = recent.Where(d => d.Date == today).ToDictionary(d => d.EmployeeId);
        foreach (var open in recent.Where(d => d.Date == yesterday && d.CheckOut is null && d.CheckIn >= new TimeOnly(12, 0)))
        {
            records.TryAdd(open.EmployeeId, open);
        }

        var names = (await employees.GetContactsAsync(tenantId, employeeIds, cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var rows = new List<LiveAttendanceStatusDto>(employeeIds.Count);
        foreach (var employeeId in employeeIds)
        {
            var name = names.GetValueOrDefault(employeeId, string.Empty);
            if (!records.TryGetValue(employeeId, out var record))
            {
                rows.Add(new LiveAttendanceStatusDto(employeeId, name, null, null, IsInside: false, null));
                continue;
            }

            var isInside = record.CheckIn is not null && record.CheckOut is null;
            rows.Add(new LiveAttendanceStatusDto(employeeId, name, record.CheckIn, record.CheckOut, isInside, record.Source));
        }

        return rows;
    }
}
