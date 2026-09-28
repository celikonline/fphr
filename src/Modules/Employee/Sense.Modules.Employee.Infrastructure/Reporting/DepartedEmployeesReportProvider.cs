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
/// Kolay İK "Hazır Şablonlar" kartı "Bu yıl işten ayrılan çalışan bilgileri" (Raporlar → İdari): seçilen yılda (yoksa
/// cari yıl) ayrılan çalışanlar - Ad, Soyad, kariyer kolonları, İşe giriş/ayrılış tarihi, kıdem (gün), yönetici,
/// çalışma şekli/sözleşme türü. <see cref="ReportEmployeeSet"/>'i "işten ayrılanları dahil et" zorunlu açık çağırır
/// (bu raporun konusu zaten ayrılanlar), sonra Status=Terminated ve TerminationDate yıl aralığında olanlara filtreler.
/// Filtreler: Yıl, Birimler. Satır kümesi çağıranın employee.employees.read kapsamıdır.
/// </summary>
public sealed class DepartedEmployeesReportProvider(
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IEmployeeContactLookup contacts,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "employee.departed";

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

        // The report's own subject is departed employees, so the "include terminated" scope switch is always on here.
        var scopedParameters = new Dictionary<string, string>(parameters) { [ReportParameterKeys.IncludeTerminated] = "true" };
        var (allRows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, EmployeePermissions.EmployeesReadKey, scopedParameters, cancellationToken);
        var rows = allRows
            .Where(r => r.Status == nameof(EmployeeStatus.Terminated) && r.TerminationDate is { } terminationDate && terminationDate >= yearStart && terminationDate <= yearEnd)
            .OrderBy(r => r.TerminationDate)
            .ToList();

        var managerIds = rows.Where(r => r.ManagerEmployeeId is not null).Select(r => r.ManagerEmployeeId!.Value).Distinct().ToList();
        var managerNames = managerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await contacts.GetContactsAsync(tenantId, managerIds, cancellationToken))
                .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("hireDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("terminationDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("tenureDays"), ExportColumnType.Number));
        columns.Add(new ExportColumn(localizer.Column("manager")));
        columns.Add(new ExportColumn(localizer.Column("employmentType")));
        columns.Add(new ExportColumn(localizer.Column("contractType")));

        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(row.HireDate);
            cells.Add(row.TerminationDate);
            cells.Add(row.HireDate is { } hireDate && row.TerminationDate is { } terminationDate ? terminationDate.DayNumber - hireDate.DayNumber : (int?)null);
            cells.Add(row.ManagerEmployeeId is { } managerId ? managerNames.GetValueOrDefault(managerId) : null);
            cells.Add(localizer.EnumText("EmploymentType", row.EmploymentType));
            cells.Add(localizer.EnumText("ContractType", row.ContractType));
            data.Add(cells);
        }

        var sheet = localizer.Sheet("departedEmployees");
        var file = exporter.ExportToExcel($"{sheet} {year}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}
