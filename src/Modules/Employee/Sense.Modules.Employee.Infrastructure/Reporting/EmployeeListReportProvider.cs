using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Employee.Infrastructure.Reporting;

/// <summary>
/// Kolay İK "Çalışan listesi raporu" (Raporlar → Çalışan; part8 #12/#33, 11.3.12): Ad, Soyad, kimlik numarası
/// (yalnız employee.sensitive.view olanlara), kariyer bilgileri (kiracının her hiyerarşi seviyesi için bir kolon),
/// işe giriş tarihi, çalışma durumu, yönetici, iletişim, çalışma şekli/sözleşme türü. Filtreler: Birimler (seçilen
/// birim + alt birimler) ve "işten ayrılanları dahil et". Satır kümesi çağıranın employee.employees.read kapsamıdır.
/// </summary>
public sealed class EmployeeListReportProvider(
    IDataScopeService dataScope,
    IOrgUnitLookup orgUnits,
    IEmployeeReportReader employees,
    IEmployeeContactLookup contacts,
    IPermissionService permissions,
    ICurrentUser currentUser,
    IReportExporter exporter,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider clock) : IReportProvider
{
    public const string ReportKey = "employee.list";

    public string Key => ReportKey;

    public string CategoryKey => ReportCategories.Employee;

    public string? RequiredPermission => EmployeePermissions.EmployeesReadKey;

    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.IncludeTerminated, ReportParameterKind.Boolean),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var (rows, hierarchy) = await new ReportEmployeeSet(dataScope, orgUnits, employees)
            .LoadAsync(tenantId, EmployeePermissions.EmployeesReadKey, parameters, cancellationToken);

        var showSensitive = await permissions.HasAsync(currentUser.UserId ?? Guid.Empty, EmployeePermissions.SensitiveViewKey, cancellationToken);
        var includeTerminated = ReportParameters.GetBool(parameters, ReportParameterKeys.IncludeTerminated);

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
        if (showSensitive)
        {
            columns.Add(new ExportColumn(localizer.Column("nationalId")));
        }

        columns.AddRange(hierarchy.Levels.Select(l => new ExportColumn(l.Name)));
        columns.Add(new ExportColumn(localizer.Column("hireDate"), ExportColumnType.Date));
        columns.Add(new ExportColumn(localizer.Column("status")));
        if (includeTerminated)
        {
            columns.Add(new ExportColumn(localizer.Column("terminationDate"), ExportColumnType.Date));
        }

        columns.Add(new ExportColumn(localizer.Column("manager")));
        columns.Add(new ExportColumn(localizer.Column("employmentType")));
        columns.Add(new ExportColumn(localizer.Column("contractType")));
        columns.Add(new ExportColumn(localizer.Column("gender")));
        if (showSensitive)
        {
            columns.Add(new ExportColumn(localizer.Column("birthDate"), ExportColumnType.Date));
        }

        columns.Add(new ExportColumn(localizer.Column("workEmail")));
        columns.Add(new ExportColumn(localizer.Column("mobilePhone")));

        var data = new List<IReadOnlyList<object?>>(rows.Count);
        foreach (var row in rows)
        {
            var cells = new List<object?> { row.FirstName, row.LastName };
            if (showSensitive)
            {
                cells.Add(row.NationalId);
            }

            cells.AddRange(hierarchy.LevelValues(row.OrgAssignments, row.OrgUnitId));
            cells.Add(row.HireDate);
            cells.Add(localizer.EnumText("EmployeeStatus", row.Status));
            if (includeTerminated)
            {
                cells.Add(row.TerminationDate);
            }

            cells.Add(row.ManagerEmployeeId is { } managerId ? managerNames.GetValueOrDefault(managerId) : null);
            cells.Add(localizer.EnumText("EmploymentType", row.EmploymentType));
            cells.Add(localizer.EnumText("ContractType", row.ContractType));
            cells.Add(localizer.EnumText("Gender", row.Gender));
            if (showSensitive)
            {
                cells.Add(row.BirthDate);
            }

            cells.Add(row.WorkEmail);
            cells.Add(row.MobilePhone);
            data.Add(cells);
        }

        var sheet = localizer.Sheet("employeeList");
        var file = exporter.ExportToExcel($"{sheet} {clock.GetUtcNow():yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }
}
