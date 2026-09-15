using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// One employee's value for one tenant-defined custom field (11.3.03 "Özel alanlar"). Dumb value holder by design - the
/// Application layer coerces/validates the raw string against the referenced CustomFieldDefinition's DataType/Options/
/// IsRequired before it ever reaches here (same spirit as <see cref="BankAccount"/> not knowing about currency rules).
/// </summary>
public sealed class CustomFieldValue : TenantEntity<Guid>
{
    internal CustomFieldValue(Guid id, Guid tenantId, Guid employeeId, Guid definitionId, string? value)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        DefinitionId = definitionId;
        Value = value;
    }

    private CustomFieldValue()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid DefinitionId { get; private set; }

    public string? Value { get; private set; }

    internal void SetValue(string? value) => Value = value;
}

/// <summary>
/// Custom field values ("Özel alanlar", E4). Like salary/bank accounts these are not auto-included: they are loaded
/// only by the custom field use cases, so freeform values never ride along with every other employee load.
/// </summary>
public sealed partial class Employee
{
    private readonly List<CustomFieldValue> _customFieldValues = [];

    /// <summary>Custom field values (EF navigation; not auto-included).</summary>
    public IReadOnlyCollection<CustomFieldValue> CustomFieldValues => _customFieldValues.AsReadOnly();

    /// <summary>
    /// Bulk-set semantics (11.3.03): only the (DefinitionId, Value) pairs present in <paramref name="values"/> change;
    /// every other stored value is left untouched. A null value clears the stored value. Returns true when anything
    /// actually changed. The caller (Application layer) has already coerced/validated each value against its
    /// CustomFieldDefinition (data type, options, required-ness, employee-editability).
    /// </summary>
    public bool SetCustomFieldValues(IReadOnlyDictionary<Guid, string?> values)
    {
        var changed = false;
        foreach (var (definitionId, value) in values)
        {
            var existing = _customFieldValues.FirstOrDefault(v => v.DefinitionId == definitionId);
            if (existing is null)
            {
                if (value is null)
                {
                    continue;
                }

                _customFieldValues.Add(new CustomFieldValue(Guid.CreateVersion7(), TenantId, Id, definitionId, value));
                changed = true;
            }
            else if (!string.Equals(existing.Value, value, StringComparison.Ordinal))
            {
                existing.SetValue(value);
                changed = true;
            }
        }

        return changed;
    }
}
