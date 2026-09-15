using Microsoft.EntityFrameworkCore;
using Sense.Modules.DataTransfer.Application;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Infrastructure.Querying;

namespace Sense.Modules.DataTransfer.Infrastructure.Persistence;

/// <summary>EF Core import job repository (tenant isolation via ModuleDbContext's global query filter).</summary>
public sealed class ImportJobRepository(DataTransferDbContext context) : IImportJobRepository, IImportJobReadStore
{
    public void Add(ImportJob job, ImportSourceFile sourceFile)
    {
        context.ImportJobs.Add(job);
        context.ImportSourceFiles.Add(sourceFile);
    }

    public void AddRows(IEnumerable<ImportJobRow> rows) => context.ImportJobRows.AddRange(rows);

    public Task<ImportJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.ImportJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task<ImportSourceFile?> GetSourceFileAsync(Guid importJobId, CancellationToken cancellationToken = default) =>
        context.ImportSourceFiles.AsNoTracking().FirstOrDefaultAsync(f => f.ImportJobId == importJobId, cancellationToken);

    public Task<PagedResult<ImportJob>> ListAsync(PagedQuery paging, CancellationToken ct) =>
        context.ImportJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .ToPagedResultAsync(paging, ct);

    public Task<PagedResult<ImportJobRow>> ListRowsAsync(Guid importJobId, ImportJobRowOutcome? outcome, PagedQuery paging, CancellationToken ct)
    {
        var query = context.ImportJobRows.AsNoTracking().Where(r => r.ImportJobId == importJobId);
        if (outcome is { } value)
        {
            query = query.Where(r => r.Outcome == value);
        }

        return query.OrderBy(r => r.RowNumber).ToPagedResultAsync(paging, ct);
    }

    public async Task<IReadOnlyList<ImportJobRow>> ListAllRowsAsync(Guid importJobId, CancellationToken ct) =>
        await context.ImportJobRows.AsNoTracking().Where(r => r.ImportJobId == importJobId).OrderBy(r => r.RowNumber).ToListAsync(ct);
}
