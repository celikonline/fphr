using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Employee.Application.Import;

/// <summary>Column keys of the "employee.master" template (written to the template's hidden key row).</summary>
public static class EmployeeImportColumns
{
    public const string TemplateCode = "employee.master";
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string NationalId = "nationalId";
    public const string WorkEmail = "workEmail";
    public const string PersonalEmail = "personalEmail";
    public const string WorkPhone = "workPhone";
    public const string HireDate = "hireDate";
    public const string BirthDate = "birthDate";
    public const string Manager = "manager";

    // Personnel fields added by E1/E2, offered by the template since E6.
    public const string LeaveAccrualStartDate = "leaveAccrualStartDate";
    public const string Gender = "gender";
    public const string MaritalStatus = "maritalStatus";
    public const string ChildCount = "childCount";
    public const string EducationStatus = "educationStatus";
    public const string HighestEducationLevel = "highestEducationLevel";
    public const string MilitaryStatus = "militaryStatus";
    public const string DisabilityDegree = "disabilityDegree";
    public const string BloodType = "bloodType";
    public const string MobilePhone = "mobilePhone";
    public const string HomePhone = "homePhone";
    public const string AddressLine = "addressLine";
    public const string City = "city";
    public const string PostalCode = "postalCode";
    public const string ContractType = "contractType";
    public const string EmploymentType = "employmentType";

    // Salary block ("Maaş bloğu", E6 part 2): one salary record per row; the whole block is ignored (not an error)
    // unless both SalaryEffectiveFrom and SalaryAmount are filled (11.3.03 "Maaş bloğu bütünlüğü").
    public const string SalaryEffectiveFrom = "salaryEffectiveFrom";
    public const string SalaryAmount = "salaryAmount";
    public const string SalaryCurrency = "salaryCurrency";
    public const string SalaryType = "salaryType";
    public const string PayFrequency = "payFrequency";
    public const string TransportAllowance = "transportAllowance";
    public const string MealAllowance = "mealAllowance";

    /// <summary>Dynamic column per active hierarchy level: "orgUnit:{OrgUnitType.Code}".</summary>
    public const string OrgUnitPrefix = "orgUnit:";

    public static string OrgUnit(string typeCode) => OrgUnitPrefix + typeCode;

    /// <summary>Dynamic column per active custom field definition: "customField:{CustomFieldDefinition.Key}".</summary>
    public const string CustomFieldPrefix = "customField:";

    public static string CustomField(string key) => CustomFieldPrefix + key;

    public static string HeaderKey(string key) => "import.column.employee." + key;
}

/// <summary>"Çalışanlar" import template (Faz 0 item 7/8): identity/contact fields, manager by full name, one column per active org level.</summary>
public sealed class EmployeeMasterImportTemplate(
    ITenantContext tenant, IOrgUnitLookup orgUnits, ICustomFieldDefinitionRepository customFields) : IImportTemplate
{
    private const string KeyPrefix = "import.template.employee.master.";

    public string Code => EmployeeImportColumns.TemplateCode;

    public string NameKey => KeyPrefix + "name";

    public string DescriptionKey => KeyPrefix + "description";

    public string RequiredPermission => EmployeePermissions.EmployeesImportKey;

    public IReadOnlyList<ImportMode> SupportedModes { get; } = [ImportMode.Upsert, ImportMode.UpdateOnly];

    public IReadOnlyList<string> InstructionKeys { get; } =
    [
        KeyPrefix + "instruction.match",
        KeyPrefix + "instruction.new_employee",
        KeyPrefix + "instruction.manager",
        KeyPrefix + "instruction.org_units",
    ];

    public async Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default)
    {
        var columns = new List<ImportColumn>
        {
            Static(EmployeeImportColumns.FirstName, ImportColumnDataType.Text, ImportColumnRequirement.Always),
            Static(EmployeeImportColumns.LastName, ImportColumnDataType.Text, ImportColumnRequirement.Always),
            Static(EmployeeImportColumns.NationalId, ImportColumnDataType.NationalId, ImportColumnRequirement.Always, isMatchKey: true),
            Static(EmployeeImportColumns.WorkEmail, ImportColumnDataType.Email),
            Static(EmployeeImportColumns.PersonalEmail, ImportColumnDataType.Email),
            Static(EmployeeImportColumns.WorkPhone, ImportColumnDataType.Phone),
            Static(EmployeeImportColumns.HireDate, ImportColumnDataType.Date),
            Static(EmployeeImportColumns.BirthDate, ImportColumnDataType.Date),
            Static(EmployeeImportColumns.Manager, ImportColumnDataType.Lookup),
            Static(EmployeeImportColumns.LeaveAccrualStartDate, ImportColumnDataType.Date),
            Options<Gender>(EmployeeImportColumns.Gender),
            Options<MaritalStatus>(EmployeeImportColumns.MaritalStatus),
            Static(EmployeeImportColumns.ChildCount, ImportColumnDataType.Text),
            Options<EducationStatus>(EmployeeImportColumns.EducationStatus),
            Options<HighestEducationLevel>(EmployeeImportColumns.HighestEducationLevel),
            Options<MilitaryStatus>(EmployeeImportColumns.MilitaryStatus),
            Options<DisabilityDegree>(EmployeeImportColumns.DisabilityDegree),
            Options<BloodType>(EmployeeImportColumns.BloodType),
            Static(EmployeeImportColumns.MobilePhone, ImportColumnDataType.Phone),
            Static(EmployeeImportColumns.HomePhone, ImportColumnDataType.Phone),
            Static(EmployeeImportColumns.AddressLine, ImportColumnDataType.Text),
            Static(EmployeeImportColumns.City, ImportColumnDataType.Text),
            Static(EmployeeImportColumns.PostalCode, ImportColumnDataType.Text),
            Options<ContractType>(EmployeeImportColumns.ContractType),
            Options<EmploymentType>(EmployeeImportColumns.EmploymentType),
            Static(EmployeeImportColumns.SalaryEffectiveFrom, ImportColumnDataType.Date),
            Static(EmployeeImportColumns.SalaryAmount, ImportColumnDataType.Text),
            Static(EmployeeImportColumns.SalaryCurrency, ImportColumnDataType.Text),
            Options<SalaryType>(EmployeeImportColumns.SalaryType),
            Options<PayFrequency>(EmployeeImportColumns.PayFrequency),
            Static(EmployeeImportColumns.TransportAllowance, ImportColumnDataType.Text),
            Static(EmployeeImportColumns.MealAllowance, ImportColumnDataType.Text),
        };

        var types = await orgUnits.ListActiveTypesAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        var units = await orgUnits.ListActiveUnitsAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        var namesByType = units.ToLookup(u => u.TypeId, u => u.Name);
        foreach (var type in types)
        {
            var options = namesByType[type.Id]
                .DistinctBy(ImportValueParser.MatchKey)
                .OrderBy(n => n, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))
                .ToList();
            columns.Add(new ImportColumn(
                EmployeeImportColumns.OrgUnit(type.Code), HeaderKey: null, HeaderText: type.Name, ImportColumnDataType.Lookup,
                ImportColumnRequirement.Optional, Options: options, DescriptionKey: EmployeeImportColumns.HeaderKey("orgUnit") + ".description"));
        }

        // One column per active custom field definition ("Özel alanlar", E6 part 2): header = the tenant's field
        // name (not a resource key, same as the org unit columns above), key = "customField:{definition.Key}".
        var definitions = await customFields.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        foreach (var definition in definitions.OrderBy(d => d.SortOrder).ThenBy(d => d.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true)))
        {
            var isSelect = definition.DataType is CustomFieldDataType.SingleSelect or CustomFieldDataType.MultiSelect;
            columns.Add(new ImportColumn(
                EmployeeImportColumns.CustomField(definition.Key), HeaderKey: null, HeaderText: definition.Name, CustomFieldColumnType(definition.DataType),
                ImportColumnRequirement.Optional, Options: isSelect ? definition.Options : null,
                DescriptionKey: EmployeeImportColumns.HeaderKey("customField") + ".description"));
        }

        return columns;
    }

    /// <summary>
    /// No numeric/boolean data type exists in <see cref="ImportColumnDataType"/> (see <see cref="EmployeeImportColumns.ChildCount"/>
    /// for the same choice), so Text/Number/Boolean custom fields are offered as free text and coerced/validated by
    /// <see cref="Sense.Modules.Employee.Application.Employees.CustomFieldValueCoercion"/> on import.
    /// </summary>
    private static ImportColumnDataType CustomFieldColumnType(CustomFieldDataType dataType) => dataType switch
    {
        CustomFieldDataType.Date => ImportColumnDataType.Date,
        CustomFieldDataType.SingleSelect or CustomFieldDataType.MultiSelect => ImportColumnDataType.Lookup,
        _ => ImportColumnDataType.Text,
    };

    public IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns)
    {
        var orgValues = columns
            .Where(c => c.Key.StartsWith(EmployeeImportColumns.OrgUnitPrefix, StringComparison.Ordinal) && c.Options is { Count: > 0 })
            .Take(2)
            .ToDictionary(c => c.Key, c => c.Options![0]);

        Dictionary<string, string> Row(params (string Key, string Value)[] values)
        {
            var row = new Dictionary<string, string>(orgValues, StringComparer.Ordinal);
            foreach (var (key, value) in values)
            {
                row[key] = value;
            }

            return row;
        }

        return
        [
            new(true, Row(
                (EmployeeImportColumns.FirstName, "Ayşe"), (EmployeeImportColumns.LastName, "Yılmaz"), (EmployeeImportColumns.NationalId, "10000000146"),
                (EmployeeImportColumns.WorkEmail, "ayse.yilmaz@ornek.com"), (EmployeeImportColumns.WorkPhone, "5551234567"),
                (EmployeeImportColumns.HireDate, "01.03.2024"), (EmployeeImportColumns.BirthDate, "15/06/1990")),
                KeyPrefix + "sample.valid_new"),
            new(true, Row(
                (EmployeeImportColumns.FirstName, "Mehmet"), (EmployeeImportColumns.LastName, "Demir"), (EmployeeImportColumns.NationalId, "23456789060"),
                (EmployeeImportColumns.PersonalEmail, "mehmet.demir@ornek.com"), (EmployeeImportColumns.HireDate, "2023-09-01"),
                (EmployeeImportColumns.Manager, "Ayşe Yılmaz")),
                KeyPrefix + "sample.valid_manager"),
            new(false, Row(
                (EmployeeImportColumns.FirstName, "Zeynep"), (EmployeeImportColumns.LastName, "Kaya"), (EmployeeImportColumns.NationalId, "12345678901")),
                KeyPrefix + "sample.invalid_national_id"),
            new(false, Row(
                (EmployeeImportColumns.FirstName, "Can"), (EmployeeImportColumns.LastName, "Öztürk"), (EmployeeImportColumns.NationalId, "34567890170"),
                (EmployeeImportColumns.HireDate, "1 Ocak 2020")),
                KeyPrefix + "sample.invalid_date"),
            new(false, Row(
                (EmployeeImportColumns.FirstName, "Elif"), (EmployeeImportColumns.LastName, "Şahin"), (EmployeeImportColumns.NationalId, "10000000146"),
                (EmployeeImportColumns.WorkEmail, "elif@")),
                KeyPrefix + "sample.invalid_email"),
        ];
    }

    /// <summary>An enum column: the workbook offers the Turkish labels, the row handler also accepts the enum member name.</summary>
    private static ImportColumn Options<T>(string key)
        where T : struct, Enum =>
        new(key, EmployeeImportColumns.HeaderKey(key), HeaderText: null, ImportColumnDataType.Lookup, ImportColumnRequirement.Optional,
            Options: EmployeeImportOptions.For<T>(), DescriptionKey: EmployeeImportColumns.HeaderKey(key) + ".description");

    private static ImportColumn Static(string key, ImportColumnDataType type, ImportColumnRequirement requirement = ImportColumnRequirement.Optional, bool isMatchKey = false) =>
        new(key, EmployeeImportColumns.HeaderKey(key), HeaderText: null, type, requirement, isMatchKey, DescriptionKey: EmployeeImportColumns.HeaderKey(key) + ".description");
}
