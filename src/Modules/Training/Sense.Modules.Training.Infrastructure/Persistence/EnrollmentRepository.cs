using Microsoft.EntityFrameworkCore;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Domain;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Training.Infrastructure.Persistence;

/// <summary>EF Core-backed Enrollment repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class EnrollmentRepository(TrainingDbContext context) : IEnrollmentRepository
{
    public async Task AddAsync(Enrollment enrollment, CancellationToken ct = default)
    {
        context.Enrollments.Add(enrollment);
        await Task.CompletedTask;
    }

    public async Task<Enrollment?> GetByIdAsync(Guid tenantId, Guid enrollmentId, CancellationToken ct = default)
    {
        return await context.Enrollments
            .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<Enrollment>> GetByTrainingProgramIdAsync(Guid tenantId, Guid programId, CancellationToken ct = default)
    {
        return await context.Enrollments
            .Where(e => e.TenantId == tenantId && e.TrainingProgramId == programId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Enrollment>> GetByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await context.Enrollments
            .Where(e => e.TenantId == tenantId && e.EmployeeId == employeeId)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(Enrollment enrollment, CancellationToken ct = default)
    {
        // A tracked entity (every command handler loads it through GetByIdAsync in the same scope) is persisted by
        // change detection; DbSet.Update() would flag every property - TenantId included - as modified, which
        // AuditTenantInterceptor rightly rejects (tenant.mismatch). Only a detached entity needs marking, and
        // never its TenantId.
        var entry = context.Entry(enrollment);
        if (entry.State == EntityState.Detached)
        {
            entry.State = EntityState.Modified;
            entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;
        }

        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Enrollment enrollment, CancellationToken ct = default)
    {
        context.Enrollments.Remove(enrollment);
        await Task.CompletedTask;
    }
}
