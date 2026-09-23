using Microsoft.EntityFrameworkCore;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Training.Infrastructure.Persistence;

/// <summary>Training module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "training").</summary>
public sealed class TrainingDbContext(DbContextOptions<TrainingDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), ITrainingUnitOfWork
{
    public const string SchemaName = "training";

    public override string Schema => SchemaName;

    public DbSet<TrainingProgram> TrainingPrograms => Set<TrainingProgram>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<TrainingRequirement> TrainingRequirements => Set<TrainingRequirement>();

    /// <summary>Shared AuditLogEntry type in this module's own schema (same pattern as Leave/Expense).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(builder);
        ApplyConfigurations(builder);
    }

    private static void ApplyConfigurations(ModelBuilder builder)
    {
        // TrainingProgram configuration
        builder.Entity<TrainingProgram>(b =>
        {
            b.ToTable("TrainingPrograms");

            b.HasKey(x => x.Id);

            b.Property(x => x.TenantId)
                .IsRequired();

            b.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            b.Property(x => x.Description)
                .HasMaxLength(2000);

            b.Property(x => x.StartDate)
                .IsRequired();

            b.HasIndex(x => new { x.TenantId, x.Title })
                .IsUnique();
        });

        // Enrollment configuration
        builder.Entity<Enrollment>(b =>
        {
            b.ToTable("Enrollments");

            b.HasKey(x => x.Id);

            b.Property(x => x.TenantId)
                .IsRequired();

            b.Property(x => x.TrainingProgramId)
                .IsRequired();

            b.Property(x => x.EmployeeId)
                .IsRequired();

            b.Property(x => x.EnrolledAt)
                .IsRequired();

            b.Property(x => x.Status)
                .HasConversion<string>();

            b.Property(x => x.CancellationReason)
                .HasMaxLength(1000);

            b.HasIndex(x => new { x.TenantId, x.TrainingProgramId })
                .HasDatabaseName("IX_Enrollments_TenantId_TrainingProgramId");

            b.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("IX_Enrollments_TenantId_EmployeeId");

            // One ACTIVE enrollment per (employee, program). CancelEnrollmentHandler now keeps cancelled rows
            // (roadmap #72 — iptal geçmişi tutulsun), so the uniqueness constraint is filtered to non-cancelled
            // rows to allow re-enrollment after a cancellation. Backs TrainingErrors.DuplicateEnrollment.
            b.HasIndex(x => new { x.TenantId, x.TrainingProgramId, x.EmployeeId })
                .IsUnique()
                .HasFilter("status <> 'Cancelled'")
                .HasDatabaseName("IX_Enrollments_TenantId_TrainingProgramId_EmployeeId");
        });

        // TrainingRequirement configuration (roadmap #72: prerequisite training gating)
        builder.Entity<TrainingRequirement>(b =>
        {
            b.ToTable("TrainingRequirements");

            b.HasKey(x => x.Id);

            b.Property(x => x.TenantId)
                .IsRequired();

            b.Property(x => x.TrainingProgramId)
                .IsRequired();

            b.Property(x => x.PrerequisiteTrainingProgramId)
                .IsRequired();

            b.Property(x => x.BlocksAssignmentUntilCompleted)
                .IsRequired();

            // One prerequisite rule per (program, prerequisite) pair.
            b.HasIndex(x => new { x.TenantId, x.TrainingProgramId, x.PrerequisiteTrainingProgramId })
                .IsUnique()
                .HasDatabaseName("IX_TrainingRequirements_Program_Prerequisite");
        });
    }
}
