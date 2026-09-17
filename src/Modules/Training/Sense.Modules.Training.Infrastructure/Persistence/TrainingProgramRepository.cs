using Microsoft.EntityFrameworkCore;
using Sense.Modules.Training.Domain;

namespace Sense.Modules.Training.Infrastructure.Persistence;

/// <summary>EF Core-backed TrainingProgram repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class TrainingProgramRepository(TrainingDbContext context) : ITrainingProgramRepository
{
    public async Task AddAsync(TrainingProgram program, CancellationToken ct = default)
    {
        context.TrainingPrograms.Add(program);
        await Task.CompletedTask;
    }

    public async Task<TrainingProgram?> GetByIdAsync(Guid tenantId, Guid programId, CancellationToken ct = default)
    {
        return await context.TrainingPrograms
            .FirstOrDefaultAsync(p => p.Id == programId && p.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<TrainingProgram>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.TrainingPrograms
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(ct);
    }
}
