using System.Globalization;
using System.Text.Json;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// A custom field definition joined with one employee's stored value (used both on the employee card and as the
/// response of every custom field write - HR and self-service).
/// </summary>
public sealed record CustomFieldValueDto(
    Guid DefinitionId,
    string Key,
    string Name,
    CustomFieldDataType DataType,
    CustomFieldSection Section,
    IReadOnlyList<string> Options,
    bool IsRequired,
    bool IsEmployeeEditable,
    bool IsSensitive,
    string? Value);

/// <summary>
/// Coerces/validates a raw request value against a loaded <see cref="CustomFieldDefinition"/> (data type, options,
/// required-ness) into the canonical string stored on <see cref="Sense.Modules.Employee.Domain.Employees.CustomFieldValue"/>.
/// The Domain entity stays dumb; this is deliberately Application-layer logic (11.3.03 "Özel alanlar").
/// </summary>
public static class CustomFieldValueCoercion
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Null/blank clears the value (subject to IsRequired); otherwise the value is parsed/validated per DataType.</summary>
    public static Result<string?> Coerce(CustomFieldDefinition definition, string? rawValue)
    {
        var trimmed = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();
        if (trimmed is null)
        {
            return definition.IsRequired ? Error.Validation(CustomFieldDomainErrors.ValueRequired) : Result.Success<string?>(null);
        }

        return definition.DataType switch
        {
            CustomFieldDataType.Text => CoerceText(trimmed),
            CustomFieldDataType.Number => CoerceNumber(trimmed),
            CustomFieldDataType.Date => CoerceDate(trimmed),
            CustomFieldDataType.Boolean => CoerceBoolean(trimmed),
            CustomFieldDataType.SingleSelect => CoerceSingleSelect(trimmed, definition.Options),
            CustomFieldDataType.MultiSelect => CoerceMultiSelect(trimmed, definition.Options, definition.IsRequired),
            _ => Error.Validation(EmployeeDomainErrors.CustomFieldValueInvalid),
        };
    }

    private static Result<string?> CoerceText(string trimmed) =>
        trimmed.Length > CustomFieldLimits.ValueMaxLength
            ? Error.Validation(CustomFieldDomainErrors.ValueTooLong)
            : Result.Success<string?>(trimmed);

    private static Result<string?> CoerceNumber(string trimmed) =>
        decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? Result.Success<string?>(number.ToString(CultureInfo.InvariantCulture))
            : Error.Validation(CustomFieldDomainErrors.ValueInvalid);

    private static Result<string?> CoerceDate(string trimmed) =>
        DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Result.Success<string?>(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : Error.Validation(CustomFieldDomainErrors.ValueInvalid);

    private static Result<string?> CoerceBoolean(string trimmed) => trimmed.ToLowerInvariant() switch
    {
        "true" or "1" => Result.Success<string?>("true"),
        "false" or "0" => Result.Success<string?>("false"),
        _ => Error.Validation(CustomFieldDomainErrors.ValueInvalid),
    };

    private static Result<string?> CoerceSingleSelect(string trimmed, IReadOnlyList<string> options)
    {
        var match = options.FirstOrDefault(o => string.Equals(o, trimmed, StringComparison.CurrentCultureIgnoreCase));
        return match is null ? Error.Validation(CustomFieldDomainErrors.OptionUnknown) : Result.Success<string?>(match);
    }

    private static Result<string?> CoerceMultiSelect(string trimmed, IReadOnlyList<string> options, bool isRequired)
    {
        List<string>? requested;
        try
        {
            requested = JsonSerializer.Deserialize<List<string>>(trimmed, Json);
        }
        catch (JsonException)
        {
            return Error.Validation(CustomFieldDomainErrors.ValueInvalid);
        }

        if (requested is null || requested.Count == 0)
        {
            return isRequired ? Error.Validation(CustomFieldDomainErrors.ValueRequired) : Result.Success<string?>(null);
        }

        var canonical = new List<string>();
        foreach (var item in requested)
        {
            var match = options.FirstOrDefault(o => string.Equals(o, item, StringComparison.CurrentCultureIgnoreCase));
            if (match is null)
            {
                return Error.Validation(CustomFieldDomainErrors.OptionUnknown);
            }

            if (!canonical.Contains(match, StringComparer.Ordinal))
            {
                canonical.Add(match);
            }
        }

        return Result.Success<string?>(JsonSerializer.Serialize(canonical, Json));
    }
}

/// <summary>
/// Builds the custom field values shown on an employee card, gated per 11.3.03: sensitive fields need
/// employee.sensitive.view; otherwise the card owner or a caller who can update the employee record always sees
/// non-sensitive values, and any other reader (e.g. a manager with read-only access to someone else's card) is
/// gated by the tenant's "Erişim ayarları → Özel alanlar" toggle (AccessSettingKeys.AccountShowCustomFields).
/// </summary>
public sealed class CustomFieldValueReader(
    IEmployeeReadStore store,
    ICustomFieldDefinitionRepository definitions,
    EmployeeSensitiveAccess sensitiveAccess,
    IAccessSettingsService accessSettings,
    ICurrentUser currentUser,
    IPermissionService permissions)
{
    /// <summary>By employee id only (no EmployeeRow at hand, e.g. the HR custom-field write path) - isOwnCard is resolved as false; a caller with employee.employees.update still always sees non-sensitive values via the canManage check.</summary>
    public Task<IReadOnlyList<CustomFieldValueDto>> BuildAsync(Guid employeeId, CancellationToken ct) =>
        BuildAsync(employeeId, isOwnCard: false, ct);

    public Task<IReadOnlyList<CustomFieldValueDto>> BuildAsync(EmployeeRow row, CancellationToken ct)
    {
        var isOwnCard = row.UserId is { } userId && currentUser.UserId == userId;
        return BuildAsync(row.Id, isOwnCard, ct);
    }

    private async Task<IReadOnlyList<CustomFieldValueDto>> BuildAsync(Guid employeeId, bool isOwnCard, CancellationToken ct)
    {
        var defs = await definitions.ListActiveAsync(ct).ConfigureAwait(false);
        if (defs.Count == 0)
        {
            return [];
        }

        var canManage = currentUser.UserId is { } managerUserId &&
                        await permissions.HasAsync(managerUserId, EmployeePermissions.EmployeesUpdateKey, ct).ConfigureAwait(false);
        var canView = isOwnCard || canManage ||
                      await accessSettings.IsEnabledForCurrentUserAsync(AccessSettingKeys.AccountShowCustomFields, ct).ConfigureAwait(false);
        if (!canView)
        {
            return [];
        }

        var values = await store.GetCustomFieldValuesAsync(employeeId, ct).ConfigureAwait(false);
        var canViewSensitive = await sensitiveAccess.CanViewAsync(ct).ConfigureAwait(false);

        return defs
            .Select(d => new CustomFieldValueDto(
                d.Id, d.Key, d.Name, d.DataType, d.Section, d.Options, d.IsRequired, d.IsEmployeeEditable, d.IsSensitive,
                d.IsSensitive && !canViewSensitive ? null : values.GetValueOrDefault(d.Id)))
            .ToList();
    }
}
