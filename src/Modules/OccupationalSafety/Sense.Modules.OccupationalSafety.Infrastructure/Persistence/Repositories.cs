using Microsoft.EntityFrameworkCore;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Infrastructure.Persistence;

public sealed class WorkplaceHazardClassificationRepository(OhsDbContext context) : IWorkplaceHazardClassificationRepository
{
    public void Add(WorkplaceHazardClassification classification) => context.HazardClassifications.Add(classification);

    public async Task<WorkplaceHazardClassification?> FindCurrentAsync(Guid locationId, DateOnly asOf, CancellationToken cancellationToken = default) =>
        await context.HazardClassifications
            .Where(h => h.LocationId == locationId && h.EffectiveFrom <= asOf)
            .OrderByDescending(h => h.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkplaceHazardClassification>> ListHistoryAsync(Guid locationId, CancellationToken cancellationToken = default) =>
        await context.HazardClassifications
            .Where(h => h.LocationId == locationId)
            .OrderByDescending(h => h.EffectiveFrom)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Loads every history row and resolves "current" in memory (grouped per location): the classification
    /// table stays small per tenant (one row per location per reclassification), so this avoids a correlated
    /// subquery per location while keeping the "latest EffectiveFrom that has started" rule in one place
    /// (mirrors FindCurrentAsync).
    /// </summary>
    public async Task<IReadOnlyList<WorkplaceHazardClassification>> ListCurrentForAllLocationsAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var started = await context.HazardClassifications
            .Where(h => h.EffectiveFrom <= asOf)
            .ToListAsync(cancellationToken);

        return started
            .GroupBy(h => h.LocationId)
            .Select(g => g.OrderByDescending(h => h.EffectiveFrom).First())
            .ToList();
    }
}

public sealed class RiskAssessmentRepository(OhsDbContext context) : IRiskAssessmentRepository
{
    public void Add(RiskAssessment assessment) => context.RiskAssessments.Add(assessment);

    public async Task<Result<RiskAssessment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var assessment = await context.RiskAssessments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return assessment is null ? Error.NotFound(OhsDomainErrors.RiskAssessmentNotFound) : assessment;
    }

    public async Task<IReadOnlyList<RiskAssessment>> ListByLocationAsync(Guid locationId, CancellationToken cancellationToken = default) =>
        await context.RiskAssessments
            .Where(a => a.LocationId == locationId)
            .OrderByDescending(a => a.AssessmentDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RiskAssessment>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.RiskAssessments
            .OrderByDescending(a => a.AssessmentDate)
            .ToListAsync(cancellationToken);
}
