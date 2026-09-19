using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.MonthlyTimesheet;

public sealed record MonthlyTimesheetApprovalDto(Guid EmployeeId, DateOnly PeriodMonth, DateTime ApprovedAt, Guid? ApprovedByUserId);

/// <summary>
/// Ay sonu kapatma (senseik-029, 11.3.6 "Aylık Puantaj" S-11): önce o çalışanın ayı için izinli günde çalışma
/// taramasını (senseik-029, <see cref="LeaveOverlapChecker"/>) çalıştırır - kapatma anındaki en güncel Leave
/// verisiyle son bir çapraz kontrol - sonra MonthlyTimesheetApproval bayrağını Approve/Reapprove eder. Var olan bir
/// onayın üstüne tekrar kapatmak (örn. "Tüm Veriyi Güncelle" sonrası) bir hata değil, Reapprove'dur (domain doc:
/// "re-approving after a correction is expected").
/// </summary>
[RequiresPermission(AttendancePermissions.TimesheetApproveKey)]
public sealed record ApproveMonthlyTimesheetCommand(Guid EmployeeId, DateOnly PeriodMonth) : ICommand<MonthlyTimesheetApprovalDto>;

public sealed class ApproveMonthlyTimesheetHandler(
    IMonthlyTimesheetApprovalRepository approvals, IAttendanceDayRepository days, LeaveOverlapChecker leaveOverlap, IDataScopeService dataScope, ITenantContext tenant,
    ICurrentUser currentUser, TimeProvider clock) : ICommandHandler<ApproveMonthlyTimesheetCommand, MonthlyTimesheetApprovalDto>
{
    public async Task<Result<MonthlyTimesheetApprovalDto>> Handle(ApproveMonthlyTimesheetCommand command, CancellationToken cancellationToken)
    {
        // Handler-level data scope: [RequiresPermission] only proves the caller holds the permission, not that the employee is in their scope
        // (Manager holds attendance.timesheet.approve for their own team only).
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetApproveKey, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var monthStart = MonthlyTimesheetApproval.NormalizeToMonthStart(command.PeriodMonth);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var records = await days.ListForEmployeeAsync(command.EmployeeId, monthStart, monthEnd, cancellationToken);
        await leaveOverlap.ApplyRangeAsync(tenant.TenantId, records, monthStart, monthEnd, cancellationToken);

        var approvedAt = clock.GetUtcNow().UtcDateTime;
        var existing = await approvals.FindAsync(command.EmployeeId, monthStart, cancellationToken);
        if (existing is not null)
        {
            existing.Reapprove(approvedAt, currentUser.UserId);
            return new MonthlyTimesheetApprovalDto(existing.EmployeeId, existing.PeriodMonth, existing.ApprovedAt, existing.ApprovedByUserId);
        }

        var approval = MonthlyTimesheetApproval.Approve(tenant.TenantId, command.EmployeeId, monthStart, approvedAt, currentUser.UserId);
        approvals.Add(approval);
        return new MonthlyTimesheetApprovalDto(approval.EmployeeId, approval.PeriodMonth, approval.ApprovedAt, approval.ApprovedByUserId);
    }
}

/// <summary>Ay sonu kapatma durumu (11.3.6 "Aylık Puantaj" ekranı): var olan onaylardan yalnız bu döneme ait olanlar, boş = henüz kapatılmamış.</summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record ListMonthlyTimesheetApprovalsQuery(IReadOnlyList<Guid> EmployeeIds, DateOnly PeriodMonth) : IQuery<IReadOnlyList<MonthlyTimesheetApprovalDto>>;

public sealed class ListMonthlyTimesheetApprovalsHandler(IMonthlyTimesheetApprovalRepository approvals)
    : IQueryHandler<ListMonthlyTimesheetApprovalsQuery, IReadOnlyList<MonthlyTimesheetApprovalDto>>
{
    public async Task<Result<IReadOnlyList<MonthlyTimesheetApprovalDto>>> Handle(ListMonthlyTimesheetApprovalsQuery query, CancellationToken cancellationToken) =>
        (await approvals.ListForPeriodAsync(query.EmployeeIds, query.PeriodMonth, cancellationToken))
            .Select(a => new MonthlyTimesheetApprovalDto(a.EmployeeId, a.PeriodMonth, a.ApprovedAt, a.ApprovedByUserId))
            .ToList();
}

/// <summary>"Aylık Puantaj" ızgarası (S-11, package A5): one employee's one calendar month, combining Attendance's own
/// worked/missing/overtime totals (<see cref="IAttendanceMonthlySummaryReader"/>, already the Payroll bridge - same
/// numbers, one source of truth) with Leave's approved-day buckets (<see cref="ILeaveDayReader"/>) and this module's
/// approval flag. An employee with neither attendance rows nor approved leave in the month still gets a zeroed row
/// (unlike the two reader ports it composes) so every visible employee has a place in the grid.</summary>
public sealed record MonthlyTimesheetRowDto(
    Guid EmployeeId,
    string EmployeeName,
    decimal WorkedDays,
    decimal WorkedHours,
    decimal MissingDays,
    decimal OvertimeHours,
    decimal PaidLeaveDays,
    decimal UnpaidLeaveDays,
    decimal SickLeaveDays,
    bool IsPeriodLocked,
    bool IsApproved,
    DateTime? ApprovedAt,
    Guid? ApprovedByUserId);

[RequiresPermission("attendance.timesheet.view")]
public sealed record GetMonthlyTimesheetQuery(DateOnly PeriodMonth, Guid? OrgUnitId) : IQuery<IReadOnlyList<MonthlyTimesheetRowDto>>;

public sealed class GetMonthlyTimesheetHandler(
    ITenantContext tenant,
    IDataScopeService dataScope,
    IEmployeeDirectory employees,
    IAttendanceMonthlySummaryReader attendanceSummaries,
    ILeaveDayReader leaveDays,
    IMonthlyTimesheetApprovalRepository approvals)
    : IQueryHandler<GetMonthlyTimesheetQuery, IReadOnlyList<MonthlyTimesheetRowDto>>
{
    public async Task<Result<IReadOnlyList<MonthlyTimesheetRowDto>>> Handle(GetMonthlyTimesheetQuery query, CancellationToken cancellationToken)
    {
        var employeeIds = await VisibleTimesheetEmployees.ResolveAsync(
            dataScope, employees, tenant.TenantId, AttendancePermissions.TimesheetView.Key, query.OrgUnitId, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<MonthlyTimesheetRowDto>>([]);
        }

        var periodMonth = MonthlyTimesheetApproval.NormalizeToMonthStart(query.PeriodMonth);
        var periodEnd = periodMonth.AddMonths(1).AddDays(-1);

        var attendanceByEmployee = (await attendanceSummaries.GetAsync(tenant.TenantId, periodMonth.Year, periodMonth.Month, employeeIds, cancellationToken))
            .ToDictionary(s => s.EmployeeId);
        var leaveByEmployee = (await leaveDays.GetMonthlySummaryAsync(tenant.TenantId, periodMonth, periodEnd, employeeIds, cancellationToken))
            .ToDictionary(s => s.EmployeeId);
        var approvalByEmployee = (await approvals.ListForPeriodAsync(employeeIds, periodMonth, cancellationToken)).ToDictionary(a => a.EmployeeId);
        var names = (await employees.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var rows = employeeIds.Select(id =>
        {
            var attendance = attendanceByEmployee.GetValueOrDefault(id);
            var leave = leaveByEmployee.GetValueOrDefault(id);
            var approval = approvalByEmployee.GetValueOrDefault(id);
            return new MonthlyTimesheetRowDto(
                id,
                names.GetValueOrDefault(id, string.Empty),
                attendance?.WorkedDays ?? 0,
                attendance?.WorkedHours ?? 0,
                attendance?.MissingDays ?? 0,
                attendance?.OvertimeHours ?? 0,
                leave?.PaidLeaveDays ?? 0,
                leave?.UnpaidLeaveDays ?? 0,
                leave?.SickLeaveDays ?? 0,
                attendance?.IsPeriodLocked ?? false,
                approval is not null,
                approval?.ApprovedAt,
                approval?.ApprovedByUserId);
        }).ToList();

        return Result.Success<IReadOnlyList<MonthlyTimesheetRowDto>>(rows);
    }
}

/// <summary>"Tümünü Onayla": approves every employee visible in the grid for the month in one call, skipping rows
/// already approved this month (idempotent - re-running after adding one more visible employee only approves the
/// new one).</summary>
[RequiresPermission(AttendancePermissions.TimesheetApproveKey)]
public sealed record ApproveAllMonthlyTimesheetsCommand(DateOnly PeriodMonth, Guid? OrgUnitId) : ICommand<int>;

public sealed class ApproveAllMonthlyTimesheetsHandler(
    ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, IEmployeeDirectory employees, IMonthlyTimesheetApprovalRepository approvals,
    IAttendanceDayRepository days, LeaveOverlapChecker leaveOverlap, TimeProvider clock)
    : ICommandHandler<ApproveAllMonthlyTimesheetsCommand, int>
{
    public async Task<Result<int>> Handle(ApproveAllMonthlyTimesheetsCommand command, CancellationToken cancellationToken)
    {
        var employeeIds = await VisibleTimesheetEmployees.ResolveAsync(
            dataScope, employees, tenant.TenantId, AttendancePermissions.TimesheetApproveKey, command.OrgUnitId, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return 0;
        }

        var periodMonth = MonthlyTimesheetApproval.NormalizeToMonthStart(command.PeriodMonth);
        var alreadyApproved = (await approvals.ListForPeriodAsync(employeeIds, periodMonth, cancellationToken))
            .Select(a => a.EmployeeId).ToHashSet();
        var toApprove = employeeIds.Where(id => !alreadyApproved.Contains(id)).ToList();
        if (toApprove.Count == 0)
        {
            return 0;
        }

        // Same per-employee leave cross-check as the single Onayla (ApproveMonthlyTimesheetHandler), batched into one Leave query for the whole run.
        var monthEnd = periodMonth.AddMonths(1).AddDays(-1);
        var records = await days.ListForEmployeesAsync(toApprove, periodMonth, monthEnd, cancellationToken);
        await leaveOverlap.ApplyRangeAsync(tenant.TenantId, records, periodMonth, monthEnd, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var employeeId in toApprove)
        {
            approvals.Add(MonthlyTimesheetApproval.Approve(tenant.TenantId, employeeId, periodMonth, now, currentUser.UserId));
        }

        return toApprove.Count;
    }
}

/// <summary>Grid/approve-all share one "who is visible for this permission (+ optional org unit filter)" rule so the rows the
/// user sees are exactly the rows Tümünü Onayla touches. Empty = nobody; otherwise the list is never a Company-wide null.</summary>
internal static class VisibleTimesheetEmployees
{
    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        IDataScopeService dataScope, IEmployeeDirectory employees, Guid tenantId, string permission, Guid? orgUnitId, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(permission, cancellationToken);
        if (orgUnitId is { } unitId)
        {
            var inUnit = await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, [unitId], cancellationToken);
            visible = visible is null ? inUnit.ToHashSet() : inUnit.Where(visible.Contains).ToHashSet();
        }

        if (visible is { Count: 0 })
        {
            return [];
        }

        return visible is null
            ? await employees.ListActiveEmployeeIdsAsync(tenantId, cancellationToken)
            : visible.ToList();
    }
}
