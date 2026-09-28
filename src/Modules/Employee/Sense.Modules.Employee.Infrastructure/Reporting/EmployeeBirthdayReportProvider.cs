using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Employee.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "Hazır Şablonlar" kartı "Bu ayın doğum günü listesi" (Raporlar → Çalışan): seçilen ayda (yoksa cari ay)
/// doğum günü olan aktif çalışanlar, gün sırasına göre - doğum yılından bağımsız, her yıl tekrar eden bir liste.
/// Doğum tarihi employee.sensitive.view ile korunan bir alan olduğundan (bkz. <see cref="EmployeeListReportProvider"/>'ın
/// showSensitive kontrolü), bu rapor doğrudan o izni zorunlu kılar - aksi halde kataloğa hiç düşmez. Filtreler: Ay,
/// Birimler. Satır kümesi çağıranın employee.employees.read kapsamıdır (işten ayrılanlar hariç, varsayılan davranış).
/// </summary>
public sealed class EmployeeBirthdayReportProvider(
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "employee.birthdays";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Employee;

    public string? RequiredPermission => EmployeePermissions.SensitiveViewKey;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.Month, ReportParameterKind.Month),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var month = ReportParameters.GetInt(parameters, ReportParameterKeys.Month) is { } m and >= 1 and <= 12 ? m : today.Month;

        var (allRows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, EmployeePermissions.EmployeesReadKey, parameters, cancellationToken);
        var rows = allRows
            .Where(r => r.BirthDate is { } birthDate && birthDate.Month == month)
            .OrderBy(r => r.BirthDate!.Value.Day)
            .ToList();

        var columns = new List<ExportColumn>
        {
            new(localizer.Column("firstName")),
            new(localizer.Column("lastName")),
        };
        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("birthDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("workEmail")));
        columns.Add(new ExportColumn(localizer.Column("mobilePhone")));

        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var cells = new List<object?> { row.FirstName, row.LastName };
            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(row.BirthDate);
            cells.Add(row.WorkEmail);
            cells.Add(row.MobilePhone);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("employeeBirthdays");
        var file = exporter.ExportToExcel($"{sheet} {month:00}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}
