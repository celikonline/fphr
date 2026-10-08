using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Application;

/// <summary>Authorized field catalog for the one-time custom report builder.</summary>
[RequiresPermission("reports.custom.build")]
public sealed record GetCustomReportCatalogQuery : IQuery<CustomReportCatalog>;

public sealed class GetCustomReportCatalogHandler(
    IEnumerable<ICustomReportFieldContributor> contributors,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IPermissionService permissions) : IQueryHandler<GetCustomReportCatalogQuery, CustomReportCatalog>
{
    public async Task<Result<CustomReportCatalog>> Handle(GetCustomReportCatalogQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        if (userId == Guid.Empty || !await permissions.HasAsync(userId, ReportingPermissions.CustomBuild.Key, cancellationToken))
        {
            return Error.Forbidden(ReportingErrors.ReportPermissionDenied);
        }

        var fields = new List<CustomReportFieldDescriptor>();
        foreach (var contributor in contributors)
        {
            fields.AddRange(await contributor.GetFieldsAsync(tenant.TenantId, cancellationToken));
        }

        var allowed = CustomReportFieldCatalog.Filter(fields, await permissions.GetPermissionsAsync(userId, cancellationToken));
        var groups = allowed
            .Select(field => field.GroupKey)
            .Distinct(StringComparer.Ordinal)
            .Select(key => new CustomReportGroupDescriptor(key, $"reports:groups.{key}", CustomReportGroupOrder(key)))
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        return Result.Success(new CustomReportCatalog(groups, allowed));
    }

    private static int CustomReportGroupOrder(string key) => key switch
    {
        CustomReportKeys.EmployeeGeneral => 0,
        CustomReportKeys.EmployeeCareer => 1,
        CustomReportKeys.EmployeeCustomFields => 2,
        CustomReportKeys.LeaveRequests => 3,
        CustomReportKeys.LeaveBalance => 4,
        CustomReportKeys.LeaveOvertime => 5,
        CustomReportKeys.ExpensePayments => 6,
        _ => int.MaxValue,
    };
}

/// <summary>
/// Reports the caller may generate - every registered IReportProvider whose RequiredPermission (if any) the caller
/// holds, ordered by menu category (İdari / Çalışan / İzinler / Finans) then key. Kolay İK's unit-manager rule
/// ("maaş anahtarı kapalıysa Finans raporları listelenmez") falls out of the permission check: a caller without
/// employee.salary.view simply never sees the salary report.
/// </summary>
[RequiresPermission("reports.catalog.view")]
public sealed record ListReportCatalogQuery : IQuery<IReadOnlyList<ReportCatalogItemDto>>;

public sealed class ListReportCatalogHandler(IEnumerable<IReportProvider> providers, ICurrentUser currentUser, IPermissionService permissions)
    : IQueryHandler<ListReportCatalogQuery, IReadOnlyList<ReportCatalogItemDto>>
{
    public async Task<Result<IReadOnlyList<ReportCatalogItemDto>>> Handle(ListReportCatalogQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        var visible = new List<ReportCatalogItemDto>();
        foreach (var provider in providers)
        {
            if (provider.Key == "report.custom")
            {
                continue;
            }

            if (provider.RequiredPermission is null || await permissions.HasAsync(userId, provider.RequiredPermission, cancellationToken))
            {
                visible.Add(new ReportCatalogItemDto(provider.Key, provider.CategoryKey, provider.Parameters, provider.ExecutionMode));
            }
        }

        return Result.Success<IReadOnlyList<ReportCatalogItemDto>>(
            visible.OrderBy(v => ReportCategories.SortOrder(v.CategoryKey)).ThenBy(v => v.Key, StringComparer.Ordinal).ToList());
    }
}

/// <summary>Self-service: status/metadata of one of the caller's own report executions (not the file bytes - see GetReportExecutionFileQuery).</summary>
[RequiresPermission("reports.my.executions")]
public sealed record GetReportExecutionQuery(Guid Id) : IQuery<ReportExecutionDto>;

public sealed class GetReportExecutionHandler(IReportExecutionRepository executions, ICurrentUser currentUser)
    : IQueryHandler<GetReportExecutionQuery, ReportExecutionDto>
{
    public async Task<Result<ReportExecutionDto>> Handle(GetReportExecutionQuery query, CancellationToken cancellationToken)
    {
        var execution = await executions.GetByIdAsync(query.Id, cancellationToken);
        if (execution.IsFailure)
        {
            return execution.Error;
        }

        if (execution.Value.RequestedByUserId != (currentUser.UserId ?? Guid.Empty))
        {
            return Error.Forbidden(ReportingErrors.NotOwner);
        }

        return ReportExecutionDto.From(execution.Value);
    }
}

/// <summary>Self-service: downloads a succeeded execution's generated file.</summary>
[RequiresPermission("reports.my.executions")]
public sealed record GetReportExecutionFileQuery(Guid Id, string? Format = null) : IQuery<ReportExecutionFileDto>;

public sealed class GetReportExecutionFileHandler(
    IReportExecutionRepository executions, ICurrentUser currentUser, IReportTableReader? tableReader = null, ISimplePdfWriter? pdfWriter = null)
    : IQueryHandler<GetReportExecutionFileQuery, ReportExecutionFileDto>
{
    public async Task<Result<ReportExecutionFileDto>> Handle(GetReportExecutionFileQuery query, CancellationToken cancellationToken)
    {
        var execution = await executions.GetByIdAsync(query.Id, cancellationToken);
        if (execution.IsFailure)
        {
            return execution.Error;
        }

        if (execution.Value.RequestedByUserId != (currentUser.UserId ?? Guid.Empty))
        {
            return Error.Forbidden(ReportingErrors.NotOwner);
        }

        if (execution.Value.Status != ReportExecutionStatus.Succeeded || execution.Value.ResultContent is null)
        {
            return Error.Validation(ReportingErrors.ReportNotReady);
        }

        if (string.IsNullOrWhiteSpace(query.Format) || query.Format.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return new ReportExecutionFileDto(execution.Value.ResultFileName!, execution.Value.ResultContentType!, execution.Value.ResultContent);
        }

        if (!query.Format.Equals("pdf", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(execution.Value.ResultContentType, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation(ReportingErrors.PreviewNotAvailable);
        }

        IReadOnlyList<ExportTable> tables;
        try
        {
            tables = tableReader?.Read(execution.Value.ResultContent, cancellationToken)
                ?? throw new InvalidDataException("Report table reader is unavailable.");
        }
        catch (InvalidDataException)
        {
            return Error.Validation(ReportingErrors.PreviewNotAvailable);
        }

        var blocks = new List<SimplePdfBlock>();
        foreach (var table in tables)
        {
            const int groupWidth = 6;
            if (table.Columns.Count <= 2)
            {
                AddTableBlock(blocks, table, Enumerable.Range(0, table.Columns.Count), table.SheetName);
                continue;
            }

            for (var start = 2; start < table.Columns.Count; start += groupWidth)
            {
                if (blocks.Count > 0)
                {
                    blocks.Add(new SimplePdfPageBreak());
                }

                var indices = new[] { 0, 1 }.Concat(Enumerable.Range(start, Math.Min(groupWidth, table.Columns.Count - start)));
                AddTableBlock(blocks, table, indices, table.SheetName);
            }
        }

        var document = new SimplePdfDocument(
            Path.ChangeExtension(execution.Value.ResultFileName ?? execution.Value.ReportKey, ".pdf"),
            null,
            blocks,
            execution.Value.CompletedAt?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture),
            Landscape: true);
        var pdf = pdfWriter?.Write(document)
            ?? throw new InvalidOperationException("PDF writer is unavailable.");
        return new ReportExecutionFileDto(document.Title, "application/pdf", pdf);
    }

    private static void AddTableBlock(List<SimplePdfBlock> blocks, ExportTable table, IEnumerable<int> columnIndices, string? heading)
    {
        var indices = columnIndices.ToArray();
        blocks.Add(new SimplePdfTable(
            heading,
            indices.Select(i => table.Columns[i].Header).ToArray(),
            table.Rows.Select(row => (IReadOnlyList<string>)indices.Select(i => CellText(row[i])).ToArray()).ToArray(),
            indices.Select((column, position) => (column, position))
                .Where(x => table.Columns[x.column].Type == ExportColumnType.Number)
                .Select(x => x.position)
                .ToArray()));
    }

    private static string CellText(object? value) => value switch
    {
        null => string.Empty,
        DateOnly date => date.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture),
        DateTime date => date.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable formatted => formatted.ToString(null, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };
}

/// <summary>Self-service: the caller's own report execution history, newest first.</summary>
[RequiresPermission("reports.my.executions")]
public sealed record ListMyReportExecutionsQuery(PagedQuery Paging) : IQuery<PagedResult<ReportExecutionDto>>;

public sealed class ListMyReportExecutionsHandler(IReportExecutionRepository executions, ICurrentUser currentUser)
    : IQueryHandler<ListMyReportExecutionsQuery, PagedResult<ReportExecutionDto>>
{
    public async Task<Result<PagedResult<ReportExecutionDto>>> Handle(ListMyReportExecutionsQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        var (items, total) = await executions.ListByRequesterAsync(userId, query.Paging.Skip, query.Paging.PageSize, cancellationToken);
        return new PagedResult<ReportExecutionDto>(items.Select(ReportExecutionDto.From).ToList(), query.Paging.Page, query.Paging.PageSize, total);
    }
}
