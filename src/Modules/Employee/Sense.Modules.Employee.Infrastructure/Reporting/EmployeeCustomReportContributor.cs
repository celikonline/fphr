using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Employee.Infrastructure.Reporting;

/// <summary>Employee-owned fields for the custom report builder. It projects only selected keys into the result.</summary>
public sealed class EmployeeCustomReportContributor(
    IEmployeeReportReader employees,
    IOrgUnitLookup orgUnits,
    IStringLocalizer<SharedResource> localizer) : ICustomReportFieldContributor
{
    private const string EmployeeRead = "employee.employees.read";

    private static readonly IReadOnlyList<CustomReportFieldDescriptor> Fields =
    [
        new("employee.firstName", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.firstName", CustomReportValueType.Text, CustomReportGrain.Employee, 10, ScopePermission: EmployeeRead),
        new("employee.lastName", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.lastName", CustomReportValueType.Text, CustomReportGrain.Employee, 20, ScopePermission: EmployeeRead),
        new("employee.status", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.status", CustomReportValueType.Text, CustomReportGrain.Employee, 30, ScopePermission: EmployeeRead),
        new("employee.workEmail", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.workEmail", CustomReportValueType.Text, CustomReportGrain.Employee, 40, ScopePermission: EmployeeRead),
        new("employee.mobilePhone", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.mobilePhone", CustomReportValueType.Text, CustomReportGrain.Employee, 50, ScopePermission: EmployeeRead),
        new("employee.nationalId", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.nationalId", CustomReportValueType.Text, CustomReportGrain.Employee, 60, "employee.sensitive.view", EmployeeRead),
        new("employee.birthDate", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.birthDate", CustomReportValueType.Date, CustomReportGrain.Employee, 70, "employee.sensitive.view", EmployeeRead),
        new("employee.hireDate", CustomReportKeys.EmployeeGeneral, "reports:fields.employee.hireDate", CustomReportValueType.Date, CustomReportGrain.Employee, 80, ScopePermission: EmployeeRead),
        new("employee.orgUnit", CustomReportKeys.EmployeeCareer, "reports:fields.employee.orgUnit", CustomReportValueType.Text, CustomReportGrain.Employee, 10, ScopePermission: EmployeeRead),
    ];

    public string ModuleKey => "employee";

    public Task<IReadOnlyList<CustomReportFieldDescriptor>> GetFieldsAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Fields);

    public async Task<IReadOnlyList<CustomReportDataRow>> GetRowsAsync(CustomReportDataRequest request, CancellationToken cancellationToken)
    {
        var selectedUnits = request.OrgUnitIds?.ToArray() ?? [];
        var expandedUnits = selectedUnits.Length == 0
            ? null
            : await orgUnits.GetSelfAndDescendantIdsAsync(request.TenantId, selectedUnits, cancellationToken);
        var employeesInScope = expandedUnits is { Count: 0 }
            ? []
            : await employees.ListAsync(request.TenantId, request.EmployeeIds?.ToArray(), expandedUnits, includeTerminated: true, cancellationToken);

        IReadOnlyDictionary<Guid, string> unitNames = request.FieldKeys.Contains("employee.orgUnit")
            ? (await orgUnits.ListActiveUnitsAsync(request.TenantId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        return employeesInScope.Select(employee =>
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            AddIfSelected(values, request.FieldKeys, "employee.firstName", employee.FirstName);
            AddIfSelected(values, request.FieldKeys, "employee.lastName", employee.LastName);
            if (request.FieldKeys.Contains("employee.status"))
            {
                values.Add("employee.status", Enum.TryParse<EmployeeStatus>(employee.Status, out var status)
                    ? localizer.EnumText<EmployeeStatus>(status)
                    : employee.Status);
            }
            AddIfSelected(values, request.FieldKeys, "employee.workEmail", employee.WorkEmail);
            AddIfSelected(values, request.FieldKeys, "employee.mobilePhone", employee.MobilePhone);
            AddIfSelected(values, request.FieldKeys, "employee.nationalId", employee.NationalId);
            AddIfSelected(values, request.FieldKeys, "employee.birthDate", employee.BirthDate);
            AddIfSelected(values, request.FieldKeys, "employee.hireDate", employee.HireDate);
            AddIfSelected(values, request.FieldKeys, "employee.orgUnit", employee.OrgUnitId is { } unitId ? unitNames.GetValueOrDefault(unitId) : null);
            return new CustomReportDataRow("employee", employee.EmployeeId, null, null, values);
        }).ToArray();
    }

    private static void AddIfSelected(Dictionary<string, object?> values, IReadOnlySet<string> selected, string key, object? value)
    {
        if (selected.Contains(key))
        {
            values.Add(key, value);
        }
    }
}
