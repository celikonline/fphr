using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Modules.Performance.Domain.ReviewForms;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Performance.Infrastructure.Persistence;

/// <summary>Performance module DbContext (Performans/Hedef). Schema "performance".</summary>
public sealed class PerformanceDbContext(DbContextOptions<PerformanceDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IPerformanceUnitOfWork
{
    public const string SchemaName = "performance";

    public override string Schema => SchemaName;

    public DbSet<ReviewCycle> ReviewCycles => Set<ReviewCycle>();

    public DbSet<ReviewForm> ReviewForms => Set<ReviewForm>();

    public DbSet<Goal> Goals => Set<Goal>();

    /// <summary>Shared AuditLogEntry type in this module's own schema (same pattern as Leave/Expense).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}

internal static class PerformanceTables
{
    public const string ReviewCycles = "review_cycles";
    public const string ReviewForms = "review_forms";
    public const string ReviewFormQuestions = "review_form_questions";
    public const string Goals = "goals";

    /// <summary>String length for enum-as-string columns (Status...).</summary>
    public const int EnumCodeLength = 30;
}

public sealed class ReviewCycleConfiguration : IEntityTypeConfiguration<ReviewCycle>
{
    public void Configure(EntityTypeBuilder<ReviewCycle> b)
    {
        b.ToTable(PerformanceTables.ReviewCycles);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(PerformanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Status });

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ReviewFormConfiguration : IEntityTypeConfiguration<ReviewForm>
{
    public void Configure(EntityTypeBuilder<ReviewForm> b)
    {
        b.ToTable(PerformanceTables.ReviewForms);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(PerformanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ReviewCycleId });

        b.OwnsMany(x => x.Questions, q =>
        {
            q.ToTable(PerformanceTables.ReviewFormQuestions);
            q.WithOwner().HasForeignKey(x => x.ReviewFormId);
            q.HasKey(x => x.Id);

            // Client-generated Guid key (Guid.CreateVersion7() at add-time) - see Recruitment's FormFieldConfiguration
            // for the identical rationale: without this, a question appended to an already-tracked form in a later
            // SaveChanges can be misclassified as Modified instead of Added.
            q.Property(x => x.Id).ValueGeneratedNever();

            q.Property(x => x.Text).HasMaxLength(PerformanceLimits.QuestionTextMaxLength).IsRequired();
        });

        b.Navigation(x => x.Questions).AutoInclude();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> b)
    {
        b.ToTable(PerformanceTables.Goals);
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).HasMaxLength(PerformanceLimits.NameMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(PerformanceLimits.DescriptionMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(PerformanceTables.EnumCodeLength).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => x.ReviewCycleId);

        b.Ignore(x => x.DomainEvents);
    }
}
