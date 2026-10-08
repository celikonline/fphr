using System.Text.Json;
using Microsoft.Extensions.Localization;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;

namespace Sense.Modules.Reporting.Infrastructure;

/// <summary>Validates and composes one-time field selections through module-owned contributors.</summary>
public sealed class CustomReportProvider(
    IEnumerable<ICustomReportFieldContributor> contributors,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IDataScopeService dataScope,
    IReportExporter exporter,
    TimeProvider clock,
    IStringLocalizer<SharedResource>? localizer = null) : IReportProvider
{
    public const string ReportKey = "report.custom";
    private const string FieldKeysParameter = "fieldKeys";

    public string Key => ReportKey;
    public string ExecutionMode => "async";
    public string CategoryKey => ReportCategories.Employee;
    public string? RequiredPermission => ReportingPermissions.CustomBuild.Key;
    public string? DataScopePermission => null;
    public IReadOnlyList<ReportParameterDescriptor> Parameters { get; } =
    [
        new(FieldKeysParameter, ReportParameterKind.Select, IsRequired: true),
        new(ReportParameterKeys.OrgUnitIds, ReportParameterKind.OrgUnits),
        new(ReportParameterKeys.DateRange, ReportParameterKind.DateRange),
    ];

    public async Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        if (userId == Guid.Empty || !await permissions.HasAsync(userId, ReportingPermissions.CustomBuild.Key, cancellationToken))
        {
            throw new UnauthorizedAccessException(CustomReportErrors.FieldPermissionDenied);
        }

        if (!parameters.TryGetValue(FieldKeysParameter, out var serializedKeys))
        {
            throw new InvalidOperationException(CustomReportErrors.NoFieldsSelected);
        }

        string[] fieldKeys;
        try
        {
            fieldKeys = JsonSerializer.Deserialize<string[]>(serializedKeys) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(CustomReportErrors.UnknownField);
        }

        var allFields = new List<CustomReportFieldDescriptor>();
        var contributorList = contributors.ToArray();
        foreach (var contributor in contributorList)
        {
            allFields.AddRange(await contributor.GetFieldsAsync(tenantId, cancellationToken));
        }

        var currentPermissions = await permissions.GetPermissionsAsync(userId, cancellationToken);
        var visibleFields = CustomReportFieldCatalog.Filter(allFields, currentPermissions);
        var from = ReportParameters.GetDate(parameters, ReportParameterKeys.From);
        var to = ReportParameters.GetDate(parameters, ReportParameterKeys.To);
        var selection = CustomReportSelectionValidator.Validate(visibleFields, fieldKeys, currentPermissions, from, to);
        if (!selection.IsValid)
        {
            throw new InvalidOperationException(selection.ErrorCode);
        }

        var selectedKeys = selection.Fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var selectedPermissions = selection.Fields
            .Select(field => field.ScopePermission)
            .Where(permission => permission is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var visibleEmployeeIds = await IntersectEmployeeScopesAsync(userId, selectedPermissions, cancellationToken);

        if (!ReportParameters.TryGetGuidList(parameters, ReportParameterKeys.OrgUnitIds, out var selectedUnits))
        {
            throw new InvalidOperationException(CustomReportErrors.InvalidOrgUnits);
        }

        await ValidateOrgUnitScopeAsync(userId, selectedPermissions, selectedUnits, cancellationToken);

        var dataRequest = new CustomReportDataRequest(
            tenantId,
            visibleEmployeeIds,
            selectedUnits.Count == 0 ? null : selectedUnits.ToHashSet(),
            selectedKeys,
            from,
            to);
        var contributedRows = new List<CustomReportDataRow>();
        foreach (var contributor in contributorList)
        {
            contributedRows.AddRange(await contributor.GetRowsAsync(dataRequest, cancellationToken));
        }

        var rows = CustomReportComposer.Compose(selection.Fields, contributedRows, selection.DetailGroupKey);
        var columns = selection.Fields.Select(field => new ExportColumn(localizer?[field.LabelKey].Value ?? field.LabelKey, ToExportType(field.ValueType))).ToArray();
        var data = rows.Select(row => (IReadOnlyList<object?>)selection.Fields.Select(field => row.Values[field.Key]).ToArray()).ToArray();
        var sheet = localizer?.Sheet("customReport") ?? "Custom report";
        var file = exporter.ExportToExcel($"{sheet} {clock.GetUtcNow():yyyy-MM-dd}.xlsx", [new ExportTable(sheet, columns, data)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }

    private async Task<IReadOnlySet<Guid>?> IntersectEmployeeScopesAsync(Guid userId, IReadOnlyList<string> scopePermissions, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? intersection = null;
        foreach (var permission in scopePermissions)
        {
            if (!await permissions.HasAsync(userId, permission, cancellationToken))
            {
                throw new UnauthorizedAccessException(CustomReportErrors.FieldPermissionDenied);
            }

            var visible = await dataScope.GetVisibleEmployeeIdsAsync(permission, cancellationToken);
            if (visible is null) continue;
            intersection = intersection is null ? visible.ToHashSet() : intersection.Intersect(visible).ToHashSet();
        }

        return intersection;
    }

    private async Task ValidateOrgUnitScopeAsync(Guid userId, IReadOnlyList<string> scopePermissions, IReadOnlyList<Guid> selectedUnits, CancellationToken cancellationToken)
    {
        if (selectedUnits.Count == 0) return;

        foreach (var permission in scopePermissions)
        {
            var accessible = await dataScope.GetAccessibleOrgUnitIdsAsync(permission, cancellationToken);
            if (accessible is not null && selectedUnits.Any(unitId => !accessible.Contains(unitId)))
            {
                throw new InvalidOperationException(CustomReportErrors.InvalidOrgUnits);
            }
        }
    }

    private static ExportColumnType ToExportType(CustomReportValueType valueType) => valueType switch
    {
        CustomReportValueType.Number or CustomReportValueType.Currency => ExportColumnType.Number,
        CustomReportValueType.Date => ExportColumnType.Date,
        CustomReportValueType.Boolean => ExportColumnType.Boolean,
        _ => ExportColumnType.Text,
    };
}
