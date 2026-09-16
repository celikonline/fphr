using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Application;

/// <summary>Reports the caller may generate - every registered IReportProvider whose RequiredPermission (if any) the caller holds.</summary>
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
            if (provider.RequiredPermission is null || await permissions.HasAsync(userId, provider.RequiredPermission, cancellationToken))
            {
                visible.Add(new ReportCatalogItemDto(provider.Key));
            }
        }

        return Result.Success<IReadOnlyList<ReportCatalogItemDto>>(visible);
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
public sealed record GetReportExecutionFileQuery(Guid Id) : IQuery<ReportExecutionFileDto>;

public sealed class GetReportExecutionFileHandler(IReportExecutionRepository executions, ICurrentUser currentUser)
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

        return new ReportExecutionFileDto(execution.Value.ResultFileName!, execution.Value.ResultContentType!, execution.Value.ResultContent);
    }
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
