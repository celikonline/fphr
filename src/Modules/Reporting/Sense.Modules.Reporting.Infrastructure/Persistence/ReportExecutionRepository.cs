using Microsoft.EntityFrameworkCore;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Infrastructure.Persistence;

public sealed class ReportExecutionRepository(ReportingDbContext context) : IReportExecutionRepository
{
    public void Add(ReportExecution execution) => context.ReportExecutions.Add(execution);

    public async Task<Result<ReportExecution>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Query is automatically filtered by tenant via global filter in DbContext
        var execution = await context.ReportExecutions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return execution is null ? Error.NotFound(ReportingErrors.ReportExecutionNotFound) : execution;
    }

    public async Task<(IReadOnlyList<ReportExecution> Items, int Total)> ListByRequesterAsync(
        Guid requestedByUserId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.ReportExecutions.Where(x => x.RequestedByUserId == requestedByUserId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RequestedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
