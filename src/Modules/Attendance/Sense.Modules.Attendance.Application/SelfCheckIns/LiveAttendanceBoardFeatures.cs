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
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory employees, IAttendanceDayRepository days, TimeProvider clock)
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

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var records = (await days.ListForEmployeesAsync(employeeIds, today, today, cancellationToken)).ToDictionary(d => d.EmployeeId);
        var names = (await employees.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken))
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
