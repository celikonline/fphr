using Microsoft.EntityFrameworkCore;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Modules.Performance.Domain.ReviewForms;
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

    public async Task<IReadOnlyList<ReviewCycle>> ListAsync(ReviewCycleStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = context.ReviewCycles.AsQueryable();
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        return await query.OrderByDescending(c => c.StartDate).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed ReviewForm repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class ReviewFormRepository(PerformanceDbContext context) : IReviewFormRepository
{
    public void Add(ReviewForm form) => context.ReviewForms.Add(form);

    public async Task<Result<ReviewForm>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await context.ReviewForms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        return form is null ? Error.NotFound(PerformanceDomainErrors.FormNotFound) : form;
    }

    public async Task<IReadOnlyList<ReviewForm>> ListByCycleAsync(Guid reviewCycleId, CancellationToken cancellationToken = default) =>
        await context.ReviewForms.Where(f => f.ReviewCycleId == reviewCycleId).ToListAsync(cancellationToken);
}

/// <summary>EF Core-backed Goal repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class GoalRepository(PerformanceDbContext context) : IGoalRepository
{
    public void Add(Goal goal) => context.Goals.Add(goal);

    public async Task<Result<Goal>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var goal = await context.Goals.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        return goal is null ? Error.NotFound(PerformanceDomainErrors.GoalNotFound) : goal;
    }

    public async Task<IReadOnlyList<Goal>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Goals.Where(g => g.EmployeeId == employeeId).OrderByDescending(g => g.TargetDate).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Goal>> ListByEmployeeIdsAsync(IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        var query = context.Goals.AsQueryable();
        if (employeeIds is not null)
        {
            query = query.Where(g => employeeIds.Contains(g.EmployeeId));
        }

        return await query.OrderByDescending(g => g.TargetDate).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Goal>> ListOpenByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Goals
            .Where(g => g.EmployeeId == employeeId && (g.Status == GoalStatus.Open || g.Status == GoalStatus.InProgress))
            .ToListAsync(cancellationToken);
}
