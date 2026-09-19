using Microsoft.Extensions.Localization;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Ay sonu kapatma / mali müşavir raporu (senseik-029): mevcut <see cref="MonthlyTimesheetApproval"/> onay bayrağının
/// yanına - bordro/mali müşavire teslime hazır - o dönemin özetini üretir. attendance.monthly.summary'nin aynı
/// AttendanceMonthlyAggregate'ine, Leave.Contracts.ILeaveDayReader üzerinden ücretli/ücretsiz/raporlu izin gün
/// sayılarını ve MonthlyTimesheetApprovalRepository üzerinden onay durumunu ekler. Yalnız OKUR - onay bayrağını
/// burada set etmez (bkz. ApproveMonthlyTimesheetCommand); "Onaylanmadı" satırları mali müşavire henüz kapatılmamış
/// olduğunu gösterir, raporun kendisi bunu değiştirmez.
/// </summary>
public sealed class AttendanceMonthlyClosingReportProvider(
    IAttendanceDayRepository days,
    IMonthlyTimesheetApprovalRepository approvals,
    ILeaveDayReader leaveDays,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer) : IReportProvider
{
    public const string ReportKey = "attendance.monthly.closing";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Finance;

    public string? RequiredPermission => "attendance.timesheet.view";

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.Year, ReportParameterKind.Year, IsRequired: true),
        new(ReportParameterKeys.Month, ReportParameterKind.Month, IsRequired: true),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (from, to) = AttendanceReportPeriod.Resolve(parameters);
        var periodMonth = MonthlyTimesheetApproval.NormalizeToMonthStart(from);

        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, "attendance.timesheet.view", parameters, cancellationToken);
        var ids = rows.Select(r => r.EmployeeId).ToList();

        var records = ids.Count == 0 ? [] : await days.ListForEmployeesAsync(ids, from, to, cancellationToken);
        var recordsByEmployee = records.ToLookup(d => d.EmployeeId);

        var leaveSummaries = ids.Count == 0 ? [] : await leaveDays.GetMonthlySummaryAsync(tenantId, from, to, ids, cancellationToken);
        var leaveByEmployee = leaveSummaries.ToDictionary(s => s.EmployeeId);

        var approvalRows = ids.Count == 0 ? [] : await approvals.ListForPeriodAsync(ids, periodMonth, cancellationToken);
        var approvalByEmployee = approvalRows.ToDictionary(a => a.EmployeeId);

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("workedDays"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("workedHours"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("overtimeHours"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("nightHours"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("paidLeaveDays"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("unpaidLeaveDays"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("sickLeaveDays"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("workedWhileOnLeaveCount"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("monthClosed")));
        columns.Add(new ExportColumn(localizer.Column("closedAt"), ExportColumnType.DateTime));

        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var aggregate = AttendanceMonthlyAggregate.From(recordsByEmployee[row.EmployeeId].ToList());
            var leave = leaveByEmployee.GetValueOrDefault(row.EmployeeId);
            var approval = approvalByEmployee.GetValueOrDefault(row.EmployeeId);

            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(aggregate.WorkedDays);
            cells.Add(aggregate.WorkedHours);
            cells.Add(aggregate.OvertimeHours);
            cells.Add(aggregate.NightHours);
            cells.Add(leave?.PaidLeaveDays ?? 0m);
            cells.Add(leave?.UnpaidLeaveDays ?? 0m);
            cells.Add(leave?.SickLeaveDays ?? 0m);
            cells.Add(aggregate.WorkedWhileOnLeaveCount);
            cells.Add(localizer.EnumText("Boolean", approval is null ? "False" : "True"));
            cells.Add(approval?.ApprovedAt);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("attendanceMonthlyClosing");
        var file = exporter.ExportToExcel($"{sheet} {from:yyyy-MM}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}
