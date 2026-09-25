using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Onboarding.Application;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Onboarding.Infrastructure.Persistence;

/// <summary>Onboarding module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "onboarding").</summary>
public sealed class OnboardingDbContext(DbContextOptions<OnboardingDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IOnboardingUnitOfWork
{
    public const string SchemaName = "onboarding";

    public override string Schema => SchemaName;

    public DbSet<OnboardingTaskTemplate> TaskTemplates => Set<OnboardingTaskTemplate>();

    public DbSet<EmployeeOnboardingTask> EmployeeTasks => Set<EmployeeOnboardingTask>();

    public DbSet<OnboardingCategory> Categories => Set<OnboardingCategory>();

    public DbSet<OnboardingTemplateSet> TemplateSets => Set<OnboardingTemplateSet>();

    /// <summary>Shared AuditLogEntry type in this module's own schema (same pattern as Leave/Expense).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}

internal static class OnboardingTables
{
    public const string TaskTemplates = "task_templates";
    public const string EmployeeTasks = "employee_tasks";
    public const string Categories = "categories";
    public const string TemplateSets = "template_sets";
    public const int EnumLength = 30;
}

public sealed class OnboardingTaskTemplateConfiguration : IEntityTypeConfiguration<OnboardingTaskTemplate>
{
    public void Configure(EntityTypeBuilder<OnboardingTaskTemplate> b)
    {
        b.ToTable(OnboardingTables.TaskTemplates);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(OnboardingTables.EnumLength).IsRequired();
        b.Property(x => x.Title).HasMaxLength(OnboardingLimits.TaskTemplateTitleMaxLength).IsRequired();
        b.Property(x => x.ResponsibleRoleCode).HasMaxLength(OnboardingLimits.ResponsibleRoleCodeMaxLength).IsRequired();
        b.Property(x => x.Importance).HasConversion<string>().HasMaxLength(OnboardingTables.EnumLength).IsRequired()
            .HasDefaultValue(OnboardingImportance.Normal);
        // Category in use cannot be deleted; existing (uncategorized) templates keep a null CategoryId.
        b.HasOne<OnboardingCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.Stage });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class EmployeeOnboardingTaskConfiguration : IEntityTypeConfiguration<EmployeeOnboardingTask>
{
    public void Configure(EntityTypeBuilder<EmployeeOnboardingTask> b)
    {
        b.ToTable(OnboardingTables.EmployeeTasks);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(OnboardingTables.EnumLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(OnboardingTables.EnumLength).IsRequired();
        b.Property(x => x.WaivedReason).HasMaxLength(OnboardingLimits.WaivedReasonMaxLength);
        // One row per employee + template: backs the materializer's idempotency at the database level too.
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.TemplateId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.Property(x => x.Importance).HasConversion<string>().HasMaxLength(OnboardingTables.EnumLength).IsRequired()
            .HasDefaultValue(OnboardingImportance.Normal);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.TemplateSetId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OnboardingCategoryConfiguration : IEntityTypeConfiguration<OnboardingCategory>
{
    public void Configure(EntityTypeBuilder<OnboardingCategory> b)
    {
        b.ToTable(OnboardingTables.Categories);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(OnboardingLimits.CategoryNameMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.SortOrder });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OnboardingTemplateSetConfiguration : IEntityTypeConfiguration<OnboardingTemplateSet>
{
    public void Configure(EntityTypeBuilder<OnboardingTemplateSet> b)
    {
        b.ToTable(OnboardingTables.TemplateSets);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(OnboardingTables.EnumLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(OnboardingLimits.TemplateSetNameMaxLength).IsRequired();
        b.Property(x => x.TemplateIds).HasColumnType("uuid[]");
        b.HasIndex(x => new { x.TenantId, x.Stage });
        b.Ignore(x => x.DomainEvents);
    }
}
