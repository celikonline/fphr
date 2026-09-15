using System.Text.RegularExpressions;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.CustomFields;

/// <summary>Value type accepted by a custom field ("Özel alan tanımı genişletme", 11.3.03).</summary>
public enum CustomFieldDataType
{
    Text = 0,
    Number = 1,
    Date = 2,
    Boolean = 3,
    SingleSelect = 4,
    MultiSelect = 5,
}

/// <summary>Employee card section a custom field is grouped under.</summary>
public enum CustomFieldSection
{
    Personal = 0,
    Career = 1,
    Other = 2,
}

/// <summary>Everything about a custom field definition except its tenant-unique, immutable <see cref="CustomFieldDefinition.Key"/> and <see cref="CustomFieldDefinition.DataType"/> (set only at creation).</summary>
public sealed record CustomFieldDefinitionData(
    string Name,
    CustomFieldSection Section,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    bool IsEmployeeEditable,
    bool EmployeeEditRequiresApproval,
    bool IsReportable,
    bool IsSensitive,
    int SortOrder);

/// <summary>
/// A tenant-defined employee custom field ("Özel alanlar", 11.3.03). Own aggregate (own table): employee custom field
/// values (<see cref="Employees.CustomFieldValue"/>) reference it by id and survive its deactivation - a definition is
/// never hard-deleted because values may still reference it (<see cref="Deactivate"/>/<see cref="Reactivate"/> only).
/// <see cref="Key"/> and <see cref="DataType"/> are immutable once created; everything else (<see cref="Update"/>) is
/// editable. <see cref="Options"/> only matters for <see cref="CustomFieldDataType.SingleSelect"/>/
/// <see cref="CustomFieldDataType.MultiSelect"/> and is ignored (kept empty) for every other data type.
/// </summary>
public sealed class CustomFieldDefinition : TenantAggregateRoot<Guid>
{
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<string> _options = [];

    private CustomFieldDefinition()
    {
    }

    private CustomFieldDefinition(Guid id, Guid tenantId, string key, CustomFieldDataType dataType)
        : base(id, tenantId)
    {
        Key = key;
        DataType = dataType;
        IsActive = true;
    }

    /// <summary>Tenant-unique, slug-like (lowercase letters/digits/underscore, starting with a letter); immutable once created.</summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Immutable once created - changing it would orphan every stored value's format.</summary>
    public CustomFieldDataType DataType { get; private set; }

    public CustomFieldSection Section { get; private set; }

    /// <summary>Choice list for <see cref="CustomFieldDataType.SingleSelect"/>/<see cref="CustomFieldDataType.MultiSelect"/>; always empty for other data types.</summary>
    public IReadOnlyList<string> Options => _options;

    public bool IsRequired { get; private set; }

    /// <summary>Whether the employee may edit their own value for this field via self-service ("Self-servis düzenleme").</summary>
    public bool IsEmployeeEditable { get; private set; }

    /// <summary>
    /// Known gap (Faz 1a E4): a self-service edit is applied directly today - routing it through an approval workflow
    /// needs the Workflow module (PC-B) and is out of scope here. The flag is stored so that package can wire it up
    /// later without a data model change.
    /// </summary>
    public bool EmployeeEditRequiresApproval { get; private set; }

    public bool IsReportable { get; private set; } = true;

    /// <summary>Gated the same way as TCKN/health fields: employee.sensitive.view.</summary>
    public bool IsSensitive { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>Soft-deactivate only; see the type's remarks for why a definition is never hard-deleted.</summary>
    public bool IsActive { get; private set; }

    public static Result<CustomFieldDefinition> Create(Guid tenantId, string? key, CustomFieldDataType dataType, CustomFieldDefinitionData data, Guid? id = null)
    {
        var normalizedKey = NormalizeKey(key);
        var keyCheck = ValidateKey(normalizedKey);
        if (keyCheck.IsFailure)
        {
            return keyCheck.Error;
        }

        if (!Enum.IsDefined(dataType))
        {
            return Error.Validation(CustomFieldDomainErrors.DataTypeInvalid);
        }

        var definition = new CustomFieldDefinition(id ?? Guid.CreateVersion7(), tenantId, normalizedKey!, dataType);
        var applied = definition.Apply(data);
        return applied.IsFailure ? applied.Error : definition;
    }

    /// <summary>Everything but Key/DataType.</summary>
    public Result Update(CustomFieldDefinitionData data) => Apply(data);

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(CustomFieldDomainErrors.AlreadyInactive);
        }

        IsActive = false;
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (IsActive)
        {
            return Error.Rule(CustomFieldDomainErrors.AlreadyActive);
        }

        IsActive = true;
        return Result.Success();
    }

    private Result Apply(CustomFieldDefinitionData data)
    {
        var name = string.IsNullOrWhiteSpace(data.Name) ? string.Empty : data.Name.Trim();
        if (name.Length == 0)
        {
            return Error.Validation(CustomFieldDomainErrors.NameRequired);
        }

        if (name.Length > CustomFieldLimits.NameMaxLength)
        {
            return Error.Validation(CustomFieldDomainErrors.NameTooLong);
        }

        if (!Enum.IsDefined(data.Section))
        {
            return Error.Validation(CustomFieldDomainErrors.SectionInvalid);
        }

        var isSelect = DataType is CustomFieldDataType.SingleSelect or CustomFieldDataType.MultiSelect;
        var options = NormalizeOptions(data.Options);
        if (isSelect && options.Count == 0)
        {
            return Error.Validation(CustomFieldDomainErrors.OptionsRequired);
        }

        if (options.Any(o => o.Length > CustomFieldLimits.OptionMaxLength))
        {
            return Error.Validation(CustomFieldDomainErrors.OptionTooLong);
        }

        if (options.Count > CustomFieldLimits.MaxOptions)
        {
            return Error.Validation(CustomFieldDomainErrors.TooManyOptions);
        }

        Name = name;
        Section = data.Section;
        _options.Clear();
        if (isSelect)
        {
            _options.AddRange(options);
        }

        IsRequired = data.IsRequired;
        IsEmployeeEditable = data.IsEmployeeEditable;
        EmployeeEditRequiresApproval = data.EmployeeEditRequiresApproval;
        IsReportable = data.IsReportable;
        IsSensitive = data.IsSensitive;
        SortOrder = data.SortOrder;
        return Result.Success();
    }

    private static List<string> NormalizeOptions(IReadOnlyList<string>? options) =>
        (options ?? [])
            .Select(o => o?.Trim() ?? string.Empty)
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string? NormalizeKey(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim().ToLowerInvariant();

    private static Result ValidateKey(string? key)
    {
        if (key is null)
        {
            return Error.Validation(CustomFieldDomainErrors.KeyRequired);
        }

        if (key.Length > CustomFieldLimits.KeyMaxLength)
        {
            return Error.Validation(CustomFieldDomainErrors.KeyTooLong);
        }

        return KeyPattern.IsMatch(key) ? Result.Success() : Error.Validation(CustomFieldDomainErrors.KeyInvalid);
    }
}

/// <summary>Domain error codes for custom field definitions.</summary>
public static class CustomFieldDomainErrors
{
    public const string NotFound = "employee.custom_field.not_found";
    public const string KeyRequired = "employee.custom_field.key_required";
    public const string KeyTooLong = "employee.custom_field.key_too_long";
    public const string KeyInvalid = "employee.custom_field.key_invalid";
    public const string KeyTaken = "employee.custom_field.key_taken";
    public const string NameRequired = "employee.custom_field.name_required";
    public const string NameTooLong = "employee.custom_field.name_too_long";
    public const string SectionInvalid = "employee.custom_field.section_invalid";
    public const string DataTypeInvalid = "employee.custom_field.data_type_invalid";
    public const string OptionsRequired = "employee.custom_field.options_required";
    public const string OptionTooLong = "employee.custom_field.option_too_long";
    public const string TooManyOptions = "employee.custom_field.too_many_options";
    public const string AlreadyInactive = "employee.custom_field.already_inactive";
    public const string AlreadyActive = "employee.custom_field.already_active";

    // Value-side (Application layer coercion against a loaded definition).
    public const string ValueRequired = "employee.custom_field.value_required";
    public const string ValueInvalid = "employee.custom_field.value_invalid";
    public const string ValueTooLong = "employee.custom_field.value_too_long";
    public const string OptionUnknown = "employee.custom_field.option_unknown";
    public const string NotEmployeeEditable = "employee.custom_field.not_employee_editable";
}

/// <summary>Length/count limits for custom field definitions and values.</summary>
public static class CustomFieldLimits
{
    public const int KeyMaxLength = 100;
    public const int NameMaxLength = 150;
    public const int OptionMaxLength = 100;
    public const int MaxOptions = 50;
    public const int ValueMaxLength = 1000;
}
