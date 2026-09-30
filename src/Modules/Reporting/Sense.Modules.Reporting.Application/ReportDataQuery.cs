using System.Globalization;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Application;

/// <summary>
/// Self-service: a succeeded execution's workbook as grid data (generic report screen). <c>Paging.Q</c> searches every cell,
/// <c>Paging.Sort</c> is a single column index ("2" ascending, "-2" descending), <c>Sheet</c> is the 0-based sheet index.
/// </summary>
[RequiresPermission("reports.my.executions")]
public sealed record GetReportExecutionDataQuery(Guid Id, int Sheet, PagedQuery Paging) : IQuery<ReportDataDto>;

public sealed class GetReportExecutionDataHandler(IReportExecutionRepository executions, ICurrentUser currentUser, IReportTableReader reader)
    : IQueryHandler<GetReportExecutionDataQuery, ReportDataDto>
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<Result<ReportDataDto>> Handle(GetReportExecutionDataQuery query, CancellationToken cancellationToken)
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

        if (execution.Value.ResultContentType != XlsxContentType)
        {
            return Error.Validation(ReportingErrors.PreviewNotAvailable);
        }

        IReadOnlyList<ExportTable> tables;
        try
        {
            tables = reader.Read(execution.Value.ResultContent, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Error.Validation(ReportingErrors.PreviewNotAvailable);
        }

        if (query.Sheet < 0 || query.Sheet >= tables.Count)
        {
            return Error.Validation(ReportingErrors.SheetInvalid);
        }

        var table = tables[query.Sheet];
        var sortClause = query.Paging.SortClauses.FirstOrDefault();
        int? sortColumn = null;
        if (sortClause is not null)
        {
            if (!int.TryParse(sortClause.Field, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= table.Columns.Count)
            {
                return Error.Validation(ReportingErrors.SortColumnInvalid);
            }

            sortColumn = index;
        }

        var rows = ReportTableView.Build(table, query.Paging.Q, sortColumn, sortClause?.Descending ?? false, query.Paging.Page, query.Paging.PageSize);
        return new ReportDataDto(
            tables.Select((t, i) => new ReportSheetDto(i, t.SheetName)).ToList(),
            query.Sheet,
            table.Columns.Select(c => new ReportColumnDto(c.Header, c.Type)).ToList(),
            rows);
    }
}
