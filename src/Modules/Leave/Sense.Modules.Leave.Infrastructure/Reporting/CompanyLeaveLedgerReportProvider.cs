using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Leave.Infrastructure.Reporting;

/// <summary>
/// senseik-048: şirket geneli izin/fazla mesai defterinin Excel çıktısı (Raporlar → İzinler kataloğuna da düşer).
/// Ekrandaki interaktif tabloyla (<c>GetCompanyLeaveLedgerHandler</c>) aynı <see cref="CompanyLeaveLedgerData"/>'yı
/// kullanır; buradaki tek fark web tablosunun tek "Birim yolu" sütunu yerine diğer izin raporlarıyla (bkz.
/// <see cref="LeaveBalanceSummaryReportProvider"/>) tutarlı dinamik kariyer kolonları vermesi. Ay/Yıl verilmezse
/// bugünün ayı/yılı kullanılır (ekranın varsayılanıyla aynı).
/// </summary>
public sealed class CompanyLeaveLedgerReportProvider(
    LeaveDbContext context,
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    ILegislationParameterReader legislation,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "leave.company_ledger";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Leave;

    public string? RequiredPermission => LeavePermissions.CompanyLedgerRead.Key;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.Year, ReportParameterKind.Year),
        new(ReportParameterKeys.Month, ReportParameterKind.Month),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var defaultMonthStart = new DateOnly(today.Year, today.Month, 1);
        var (monthStart, monthEnd) = ReportParameters.ResolveMonthYearRange(parameters, today) ?? (defaultMonthStart, defaultMonthStart.AddMonths(1).AddDays(-1));
        var year = ReportParameters.GetInt(parameters, ReportParameterKeys.Year) ?? monthStart.Year;

        var result = await CompanyLeaveLedgerData.BuildAsync(
            context, dataScope, orgUnits, employees, legislation, clock,
            tenantId, LeavePermissions.CompanyLedgerRead.Key, parameters, monthStart, monthEnd, year, cancellationToken);
        var hierarchy = result.Hierarchy;

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("onLeave"), ExportColumnType.Boolean));
        columns.Add(new ExportColumn(localizer.Column("leaveStart"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("leaveEnd"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("leaveType")));
        columns.Add(new ExportColumn(localizer.Column("remainingBalance"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("overtimeHoursYtd"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("overtimeLimitExceeded"), ExportColumnType.Boolean));

        var data = new List<IReadOnlyList<object?>>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(row.IsOnLeaveInPeriod);
            cells.Add(row.LeaveStartDate);
            cells.Add(row.LeaveEndDate);
            cells.Add(row.LeaveTypeName);
            cells.Add(row.AnnualLeaveBalance);
            cells.Add(row.OvertimeHoursYtd);
            cells.Add(row.OvertimeLimitExceeded);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("companyLeaveLedger");
        var file = exporter.ExportToExcel($"{sheet} {monthStart:yyyy-MM}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}
