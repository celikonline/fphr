using Sense.Modules.Employee.Domain.CustomFields;

namespace Sense.Modules.Employee.Application.CustomFields;

/// <summary>Custom field definition (admin catalog; web/src/pages/settings/custom-fields.tsx).</summary>
public sealed record CustomFieldDefinitionDto(
    Guid Id,
    string Key,
    string Name,
    CustomFieldDataType DataType,
    CustomFieldSection Section,
    IReadOnlyList<string> Options,
    bool IsRequired,
    bool IsEmployeeEditable,
    bool EmployeeEditRequiresApproval,
    bool IsReportable,
    bool IsSensitive,
    int SortOrder,
    bool IsActive)
{
    public static CustomFieldDefinitionDto From(CustomFieldDefinition d) => new(
        d.Id, d.Key, d.Name, d.DataType, d.Section, d.Options, d.IsRequired, d.IsEmployeeEditable,
        d.EmployeeEditRequiresApproval, d.IsReportable, d.IsSensitive, d.SortOrder, d.IsActive);
}
