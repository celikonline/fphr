namespace Sense.Modules.Reporting.Contracts;

public enum CustomReportValueType
{
    Text,
    Number,
    Date,
    Boolean,
    Currency,
}

public enum CustomReportGrain
{
    Employee,
    Detail,
}

/// <summary>A server-owned, stable field definition. Clients may submit only <see cref="Key"/>.</summary>
public sealed record CustomReportFieldDescriptor(
    string Key,
    string GroupKey,
    string LabelKey,
    CustomReportValueType ValueType,
    CustomReportGrain Grain,
    int SortOrder,
    string? RequiredPermission = null,
    string? ScopePermission = null);

public sealed record CustomReportGroupDescriptor(string Key, string LabelKey, int SortOrder);

public sealed record CustomReportCatalog(IReadOnlyList<CustomReportGroupDescriptor> Groups, IReadOnlyList<CustomReportFieldDescriptor> Fields);

public sealed record CustomReportDataRequest(
    Guid TenantId,
    IReadOnlySet<Guid>? EmployeeIds,
    IReadOnlySet<Guid>? OrgUnitIds,
    IReadOnlySet<string> FieldKeys,
    DateOnly? From,
    DateOnly? To);

/// <summary>Values are typed objects from a module-owned projection, never client-supplied SQL or labels.</summary>
public sealed record CustomReportDataRow(
    string GroupKey,
    Guid EmployeeId,
    string? DetailKey,
    Guid? OvertimeRequestId,
    IReadOnlyDictionary<string, object?> Values);

public interface ICustomReportFieldContributor
{
    string ModuleKey { get; }
    Task<IReadOnlyList<CustomReportFieldDescriptor>> GetFieldsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomReportDataRow>> GetRowsAsync(CustomReportDataRequest request, CancellationToken cancellationToken);
}

public static class CustomReportKeys
{
    public const string EmployeeGeneral = "employee.general";
    public const string EmployeeCareer = "employee.career";
    public const string EmployeeCustomFields = "employee.customFields";
    public const string LeaveRequests = "leave.requests";
    public const string LeaveBalance = "leave.balance";
    public const string LeaveOvertime = "leave.overtime";
    public const string ExpensePayments = "expense.payments";
    public const string OvertimePaymentReference = "overtimeRequestId";
    public const int MaximumRows = 50_000;
}

public static class CustomReportErrors
{
    public const string NoFieldsSelected = "report_execution.custom.no_fields";
    public const string UnknownField = "report_execution.custom.unknown_field";
    public const string DuplicateField = "report_execution.custom.duplicate_field";
    public const string FieldPermissionDenied = "report_execution.custom.field_permission_denied";
    public const string IncompatibleDetails = "report_execution.custom.incompatible_details";
    public const string DateRangeRequired = "report_execution.custom.date_range_required";
    public const string DateRangeInvalid = "report_execution.custom.date_range_invalid";
    public const string InvalidOrgUnits = "report_execution.custom.invalid_org_units";
    public const string RowLimitExceeded = "report_execution.custom.row_limit_exceeded";
}

public sealed record CustomReportSelectionValidation(
    string? ErrorCode,
    IReadOnlyList<CustomReportFieldDescriptor> Fields,
    string? DetailGroupKey)
{
    public bool IsValid => ErrorCode is null;
}

public static class CustomReportFieldCatalog
{
    public static IReadOnlyList<CustomReportFieldDescriptor> Filter(
        IEnumerable<CustomReportFieldDescriptor> fields,
        IReadOnlySet<string> permissions) => fields
        .Where(field => (field.RequiredPermission is null || permissions.Contains(field.RequiredPermission))
            && (field.ScopePermission is null || permissions.Contains(field.ScopePermission)))
        .OrderBy(field => field.GroupKey, StringComparer.Ordinal)
        .ThenBy(field => field.SortOrder)
        .ThenBy(field => field.Key, StringComparer.Ordinal)
        .ToArray();
}

public static class CustomReportSelectionValidator
{
    private static readonly HashSet<string> SupportedDetailPairs =
    [
        Pair(CustomReportKeys.LeaveOvertime, CustomReportKeys.ExpensePayments),
    ];

    public static CustomReportSelectionValidation Validate(
        IReadOnlyList<CustomReportFieldDescriptor> catalog,
        IReadOnlyList<string> selectedKeys,
        IReadOnlySet<string> permissions,
        DateOnly? from,
        DateOnly? to)
    {
        if (selectedKeys.Count == 0)
        {
            return Invalid(CustomReportErrors.NoFieldsSelected);
        }

        if (selectedKeys.Distinct(StringComparer.Ordinal).Count() != selectedKeys.Count)
        {
            return Invalid(CustomReportErrors.DuplicateField);
        }

        var fieldsByKey = catalog.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var fields = new List<CustomReportFieldDescriptor>(selectedKeys.Count);
        foreach (var key in selectedKeys)
        {
            if (!fieldsByKey.TryGetValue(key, out var field))
            {
                return Invalid(CustomReportErrors.UnknownField);
            }

            if (field.RequiredPermission is { } required && !permissions.Contains(required))
            {
                return Invalid(CustomReportErrors.FieldPermissionDenied);
            }

            if (field.ScopePermission is { } scope && !permissions.Contains(scope))
            {
                return Invalid(CustomReportErrors.FieldPermissionDenied);
            }

            fields.Add(field);
        }

        var detailGroups = fields
            .Where(field => field.Grain == CustomReportGrain.Detail)
            .Select(field => field.GroupKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (detailGroups.Length > 1 &&
            (detailGroups.Length != 2 || !SupportedDetailPairs.Contains(Pair(detailGroups[0], detailGroups[1]))))
        {
            return Invalid(CustomReportErrors.IncompatibleDetails);
        }

        if (detailGroups.Length > 0)
        {
            if (from is null || to is null)
            {
                return Invalid(CustomReportErrors.DateRangeRequired);
            }

            if (from > to || to > from.Value.AddMonths(12).AddDays(-1))
            {
                return Invalid(CustomReportErrors.DateRangeInvalid);
            }
        }

        return new(null, fields, detailGroups.FirstOrDefault());
    }

    private static CustomReportSelectionValidation Invalid(string error) => new(error, [], null);

    private static string Pair(string first, string second) => string.CompareOrdinal(first, second) <= 0
        ? $"{first}|{second}"
        : $"{second}|{first}";
}
