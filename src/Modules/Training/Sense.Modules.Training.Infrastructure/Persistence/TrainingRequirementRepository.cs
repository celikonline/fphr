using Microsoft.EntityFrameworkCore;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Domain;

namespace Sense.Modules.Training.Infrastructure.Persistence;

/// <summary>EF Core-backed TrainingRequirement repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class TrainingRequirementRepository(TrainingDbContext context) : ITrainingRequirementRepository
{
    public async Task AddAsync(TrainingRequirement requirement, CancellationToken cancellationToken = default)
    {
        context.TrainingRequirements.Add(requirement);
        await Task.CompletedTask;
    }

    public async Task<TrainingRequirement?> GetByIdAsync(Guid tenantId, Guid requirementId, CancellationToken cancellationToken = default)
    {
        return await context.TrainingRequirements
            .FirstOrDefaultAsync(r => r.Id == requirementId && r.TenantId == tenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<TrainingRequirement>> GetByTrainingProgramIdAsync(Guid tenantId, Guid programId, CancellationToken cancellationToken = default)
    {
        return await context.TrainingRequirements
            .Where(r => r.TenantId == tenantId && r.TrainingProgramId == programId)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(TrainingRequirement requirement, CancellationToken cancellationToken = default)
    {
        context.TrainingRequirements.Remove(requirement);
        await Task.CompletedTask;
    }
}
