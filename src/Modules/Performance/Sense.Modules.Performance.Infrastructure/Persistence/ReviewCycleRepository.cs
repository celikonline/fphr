using Microsoft.EntityFrameworkCore;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Infrastructure.Persistence;

/// <summary>EF Core-backed ReviewCycle repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class ReviewCycleRepository(PerformanceDbContext context) : IReviewCycleRepository
{
    public void Add(ReviewCycle cycle) => context.ReviewCycles.Add(cycle);

    public async Task<Result<ReviewCycle>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cycle = await context.ReviewCycles.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return cycle is null ? Error.NotFound(PerformanceDomainErrors.CycleNotFound) : cycle;
    }

    public async Task<IReadOnlyList<ReviewCycle>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.ReviewCycles.OrderByDescending(c => c.PeriodStart).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewCycle>> ListWithAssignmentsForEvaluatorAsync(Guid evaluatorEmployeeId, CancellationToken cancellationToken = default) =>
        await context.ReviewCycles
            .Where(c => c.Participants.Any(p => p.Assignments.Any(a => a.EvaluatorEmployeeId == evaluatorEmployeeId)))
            .ToListAsync(cancellationToken);
}
