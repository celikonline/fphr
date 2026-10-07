using Microsoft.EntityFrameworkCore;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Domain;
using Sense.Shared.Kernel.Domain;

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

    public async Task UpdateAsync(TrainingProgram program, CancellationToken ct = default)
    {
        // A tracked entity (every command handler loads it through GetByIdAsync in the same scope) is persisted by
        // change detection; DbSet.Update() would flag every property - TenantId included - as modified, which
        // AuditTenantInterceptor rightly rejects (tenant.mismatch). Only a detached entity needs marking, and
        // never its TenantId.
        var entry = context.Entry(program);
        if (entry.State == EntityState.Detached)
        {
            entry.State = EntityState.Modified;
            entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;
        }

        await Task.CompletedTask;
    }

    public async Task DeleteAsync(TrainingProgram program, CancellationToken ct = default)
    {
        context.TrainingPrograms.Remove(program);
        await Task.CompletedTask;
    }
}
