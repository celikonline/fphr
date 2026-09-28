using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Employee.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "Hazır Şablonlar" kartı "Bu yılın çalışan dönüşüm (Turnover) raporu" (Raporlar → İdari), indirilebilir
/// versiyonu: Analytics'in <c>TURNOVER_RATE</c> panosuyla aynı yaklaşıklığı kullanır (dönemin ortalama kadrosu yerine
/// bugünkü aktif kadro - henüz zaman noktalı geçmiş kadro sorgusu yok, WorkforceKpiFeatures.BuildTurnoverRate ile
/// tutarlı). İki sayfa: "Özet" (tek satır oran) ve "Ayrılanlar" (yılda ayrılan çalışanların dökümü, kolonları
/// <see cref="DepartedEmployeesReportProvider"/> ile aynı). Filtreler: Yıl, Birimler. Satır kümesi çağıranın
/// employee.employees.read kapsamıdır.
/// </summary>
public sealed class EmployeeTurnoverReportProvider(
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IEmployeeContactLookup contacts,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "employee.turnover";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Administrative;

    public string? RequiredPermission => EmployeePermissions.EmployeesReadKey;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.Year, ReportParameterKind.Year),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var year = ReportParameters.GetInt(parameters, ReportParameterKeys.Year) ?? today.Year;
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var scopedParameters = new Dictionary<string, string>(parameters) { [ReportParameterKeys.IncludeTerminated] = "true" };
        var (allRows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, EmployeePermissions.EmployeesReadKey, scopedParameters, cancellationToken);

        var activeHeadcount = allRows.Count(r => r.Status != nameof(EmployeeStatus.Terminated));
        var departed = allRows
            .Where(r => r.Status == nameof(EmployeeStatus.Terminated) && r.TerminationDate is { } terminationDate && terminationDate >= yearStart && terminationDate <= yearEnd)
            .OrderBy(r => r.TerminationDate)
            .ToList();
        var turnoverRate = activeHeadcount == 0 ? 0m : Math.Round((decimal)departed.Count / activeHeadcount, 4);

        var managerIds = departed.Where(r => r.ManagerEmployeeId is not null).Select(r => r.ManagerEmployeeId!.Value).Distinct().ToList();
        var managerNames = managerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await contacts.GetContactsAsync(tenantId, managerIds, cancellationToken))
                .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var summaryColumns = new List<ExportColumn>
        {
            new(localizer.Column("year"), ExportColumnType.Number),
            new(localizer.Column("terminationCount"), ExportColumnType.Number),
            new(localizer.Column("activeHeadcount"), ExportColumnType.Number),
            new(localizer.Column("turnoverRate"), ExportColumnType.Number),
        };
        var summaryData = new List<IReadOnlyList<object?>> { new List<object?> { year, departed.Count, activeHeadcount, turnoverRate } };

        var detailColumns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        detailColumns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        detailColumns.Add(new ExportColumn(localizer.Column("hireDate"), ExportColumnType.Date));
        detailColumns.Add(new ExportColumn(localizer.Column("terminationDate"), ExportColumnType.Date));
        detailColumns.Add(new ExportColumn(localizer.Column("manager")));

        var detailData = new List<IReadOnlyList<object?>>(departed.Count);
        foreach (var row in departed)
        {
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(row.HireDate);
            cells.Add(row.TerminationDate);
            cells.Add(row.ManagerEmployeeId is { } managerId ? managerNames.GetValueOrDefault(managerId) : null);
            detailData.Add(cells);
        }

        var summarySheet = localizer.Sheet("turnoverSummary");
        var detailSheet = localizer.Sheet("turnoverDetail");
        var file = exporter.ExportToExcel(
            $"{summarySheet} {year}.xlsx",
            [new ExportTable(summarySheet, summaryColumns, summaryData), new ExportTable(detailSheet, detailColumns, detailData)],
            cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}
