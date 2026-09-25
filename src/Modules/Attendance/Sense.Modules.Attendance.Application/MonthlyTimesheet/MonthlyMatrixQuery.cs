using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.MonthlyTimesheet;

/// <summary>Aylık puantaj matrisi (Z3.4) hücre kodları: web sözleşmesiyle KAPALI küme (yeni kod = sözleşme değişikliği).</summary>
public static class MatrixCellCodes
{
    public const string Absent = "absent";
    public const string Missing = "missing";
    public const string Overtime = "overtime";
    public const string Full = "full";
    public const string WeeklyRest = "weeklyRest";
    public const string RestDay = "restDay";
    public const string PaidLeave = "paidLeave";
    public const string UnpaidLeave = "unpaidLeave";
    public const string OtherLeave = "otherLeave";
    public const string PublicHoliday = "publicHoliday";
    public const string Incalculable = "incalculable";
    public const string None = "none";
}

public static class MonthlyMatrixLimits
{
    public const int DefaultPageSize = 200;
    public const int MaxPageSize = 500;
}

public sealed record MatrixCellDto(DateOnly Date, string Code);

public sealed record MatrixRowDto(Guid EmployeeId, string EmployeeName, IReadOnlyList<MatrixCellDto> Cells);

/// <summary><see cref="IsLocked"/>: dönem başına tek bayrak - sayfadaki değil, kapsamdaki TÜM çalışanlar için o ay MonthlyTimesheetApproval var.</summary>
public sealed record MonthlyMatrixDto(IReadOnlyList<MatrixRowDto> Rows, bool IsLocked);

[RequiresPermission("attendance.daily.view")]
public sealed record GetMonthlyMatrixQuery(DateOnly Period, Guid? OrgUnitId, int Page = 1, int PageSize = MonthlyMatrixLimits.DefaultPageSize) : IQuery<MonthlyMatrixDto>;

/// <summary>
/// Çalışan × gün matrisi. Öncelik: kayıt varsa (hesaplanamayan, izin, eksik, fazla mesai, tam); kayıt yoksa (izin, plan gün türü,
/// resmi tatil takvimi, çalışma takvimi dışı = haftalık tatil, geçmiş çalışma günü = devamsız, bugün/gelecek = none).
/// İzin türü ayrımı için <see cref="ILeaveOnDateReader"/> opsiyoneldir: yoksa izinli gün "paidLeave" sayılır; ücretsiz → unpaidLeave,
/// raporlu (Sick) → otherLeave, kalanı paidLeave.
/// </summary>
public sealed class GetMonthlyMatrixHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory employees, IAttendanceDayRepository days, IShiftAssignmentRepository assignments,
    IWorkScheduleReadService schedules, IMonthlyTimesheetApprovalRepository approvals, NonWorkingDayResolver nonWorkingDays, ITenantLocalClock localClock,
    ILeaveOnDateReader? leaveOnDate = null)
    : IQueryHandler<GetMonthlyMatrixQuery, MonthlyMatrixDto>
{
    public async Task<Result<MonthlyMatrixDto>> Handle(GetMonthlyMatrixQuery query, CancellationToken cancellationToken)
    {
        var visibleIds = await VisibleEmployeeResolver.ResolveAsync(
            AttendancePermissions.DailyView.Key, query.OrgUnitId, tenant.TenantId, dataScope, employees, cancellationToken);
        if (visibleIds.Count == 0)
        {
            return new MonthlyMatrixDto([], false);
        }

        var monthStart = MonthlyTimesheetApproval.NormalizeToMonthStart(query.Period);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var names = (await employees.GetContactsAsync(tenant.TenantId, visibleIds, cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());
        var ordered = visibleIds
            .OrderBy(id => names.GetValueOrDefault(id, string.Empty), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(id => id)
            .ToList();

        var approvedIds = (await approvals.ListForPeriodAsync(ordered, monthStart, cancellationToken)).Select(a => a.EmployeeId).ToHashSet();
        var isLocked = ordered.All(approvedIds.Contains);

        var pageSize = Math.Clamp(query.PageSize, 1, MonthlyMatrixLimits.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        var pageIds = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        if (pageIds.Count == 0)
        {
            return new MonthlyMatrixDto([], isLocked);
        }

        var records = (await days.ListForEmployeesAsync(pageIds, monthStart, monthEnd, cancellationToken)).ToLookup(d => d.EmployeeId);
        var planned = (await assignments.ListForRangeAsync(monthStart, monthEnd, pageIds, cancellationToken)).ToLookup(a => a.EmployeeId);
        var nonWorking = await nonWorkingDays.ResolveAsync(tenant.TenantId, pageIds, monthStart, monthEnd, cancellationToken);
        var leaveCodes = await ResolveLeaveCodesAsync(pageIds, monthStart, monthEnd, nonWorking, cancellationToken);
        var localToday = DateOnly.FromDateTime((await localClock.NowAsync(tenant.TenantId, cancellationToken)).Local);

        var rows = new List<MatrixRowDto>(pageIds.Count);
        foreach (var employeeId in pageIds)
        {
            var byDate = records[employeeId].ToDictionary(d => d.Date);
            var planByDate = planned[employeeId].GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.First());
            var workingDays = (await schedules.GetWorkingDaysAsync(employeeId, monthStart, monthEnd, cancellationToken)).ToHashSet();

            var cells = new List<MatrixCellDto>(monthEnd.Day);
            for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
            {
                var leaveCode = nonWorking.IsOnLeave(employeeId, date) ? leaveCodes.GetValueOrDefault((employeeId, date), MatrixCellCodes.PaidLeave) : null;
                cells.Add(new MatrixCellDto(date, byDate.TryGetValue(date, out var record)
                    ? CodeForRecord(record, leaveCode)
                    : CodeForEmptyDay(date, leaveCode, planByDate.GetValueOrDefault(date), nonWorking.IsHoliday(date), workingDays.Contains(date), localToday)));
            }

            rows.Add(new MatrixRowDto(employeeId, names.GetValueOrDefault(employeeId, string.Empty), cells));
        }

        return new MonthlyMatrixDto(rows, isLocked);
    }

    private async Task<Dictionary<(Guid, DateOnly), string>> ResolveLeaveCodesAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, NonWorkingDays nonWorking, CancellationToken cancellationToken)
    {
        var codes = new Dictionary<(Guid, DateOnly), string>();
        if (leaveOnDate is null)
        {
            return codes;
        }

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (!employeeIds.Any(id => nonWorking.IsOnLeave(id, date)))
            {
                continue;
            }

            foreach (var leave in await leaveOnDate.ListAsync(tenant.TenantId, date, employeeIds, cancellationToken))
            {
                codes[(leave.EmployeeId, date)] = !leave.IsPaid ? MatrixCellCodes.UnpaidLeave
                    : string.Equals(leave.LeaveTypeCode, "Sick", StringComparison.OrdinalIgnoreCase) ? MatrixCellCodes.OtherLeave
                    : MatrixCellCodes.PaidLeave;
            }
        }

        return codes;
    }

    private static string CodeForRecord(AttendanceDay record, string? leaveCode)
    {
        if ((record.Anomalies & AttendanceAnomaly.Incalculable) != 0)
        {
            return MatrixCellCodes.Incalculable;
        }

        if (leaveCode is not null)
        {
            return leaveCode;
        }

        if ((record.Anomalies & AttendanceAnomaly.NoData) != 0)
        {
            return MatrixCellCodes.Absent;
        }

        if ((record.Anomalies & (AttendanceAnomaly.MissingCheckIn | AttendanceAnomaly.MissingCheckOut)) != 0)
        {
            return MatrixCellCodes.Missing;
        }

        return (record.Anomalies & (AttendanceAnomaly.Overwork | AttendanceAnomaly.LateLeave)) != 0 || record.OvertimeMinutes > 0
            ? MatrixCellCodes.Overtime
            : MatrixCellCodes.Full;
    }

    private static string CodeForEmptyDay(DateOnly date, string? leaveCode, ShiftAssignment? plan, bool isHoliday, bool isScheduledWorkingDay, DateOnly localToday)
    {
        if (leaveCode is not null)
        {
            return leaveCode;
        }

        switch (plan?.DayType)
        {
            case ShiftDayType.WeeklyRest:
                return MatrixCellCodes.WeeklyRest;
            case ShiftDayType.RestDay:
                return MatrixCellCodes.RestDay;
            case ShiftDayType.PublicHoliday:
                return MatrixCellCodes.PublicHoliday;
        }

        if (isHoliday)
        {
            return MatrixCellCodes.PublicHoliday;
        }

        if (plan is null && !isScheduledWorkingDay)
        {
            return MatrixCellCodes.WeeklyRest;
        }

        return date < localToday ? MatrixCellCodes.Absent : MatrixCellCodes.None;
    }
}
