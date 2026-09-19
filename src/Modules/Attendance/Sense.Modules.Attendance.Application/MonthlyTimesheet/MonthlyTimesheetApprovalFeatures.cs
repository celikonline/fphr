using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
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
    IMonthlyTimesheetApprovalRepository approvals, IAttendanceDayRepository days, LeaveOverlapChecker leaveOverlap, ITenantContext tenant, ICurrentUser currentUser,
    TimeProvider clock) : ICommandHandler<ApproveMonthlyTimesheetCommand, MonthlyTimesheetApprovalDto>
{
    public async Task<Result<MonthlyTimesheetApprovalDto>> Handle(ApproveMonthlyTimesheetCommand command, CancellationToken cancellationToken)
    {
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
