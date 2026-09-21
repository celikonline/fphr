using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.OccupationalSafety.Application;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.OccupationalSafety.Infrastructure.Persistence;

/// <summary>Occupational Safety (İSG) module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "ohs").</summary>
public sealed class OhsDbContext(DbContextOptions<OhsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IOhsUnitOfWork
{
    public const string SchemaName = "ohs";

    public override string Schema => SchemaName;

    public DbSet<WorkplaceHazardClassification> HazardClassifications => Set<WorkplaceHazardClassification>();

    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();

    /// <summary>Shared AuditLogEntry type in this module's own schema (same pattern as Leave/Expense).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}

internal static class OhsTables
{
    public const string HazardClassifications = "hazard_classifications";
    public const string RiskAssessments = "risk_assessments";

    public const int HazardClassLength = 30;
    public const int NoteLength = 1000;
    public const int PreparedByLength = 255;
}

public sealed class WorkplaceHazardClassificationConfiguration : IEntityTypeConfiguration<WorkplaceHazardClassification>
{
    public void Configure(EntityTypeBuilder<WorkplaceHazardClassification> b)
    {
        b.ToTable(OhsTables.HazardClassifications);
        b.HasKey(x => x.Id);

        b.Property(x => x.HazardClass).HasConversion<string>().HasMaxLength(OhsTables.HazardClassLength).IsRequired();
        b.Property(x => x.Note).HasMaxLength(OhsTables.NoteLength);

        b.HasIndex(x => new { x.TenantId, x.LocationId });
        b.HasIndex(x => new { x.TenantId, x.LocationId, x.EffectiveFrom }).IsUnique();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RiskAssessmentConfiguration : IEntityTypeConfiguration<RiskAssessment>
{
    public void Configure(EntityTypeBuilder<RiskAssessment> b)
    {
        b.ToTable(OhsTables.RiskAssessments);
        b.HasKey(x => x.Id);

        b.Property(x => x.HazardClassAtAssessment).HasConversion<string>().HasMaxLength(OhsTables.HazardClassLength).IsRequired();
        b.Property(x => x.PreparedBy).HasMaxLength(OhsTables.PreparedByLength).IsRequired();
        b.Property(x => x.Note).HasMaxLength(OhsTables.NoteLength);
        b.Ignore(x => x.NextDueDate);

        b.HasIndex(x => new { x.TenantId, x.LocationId });

        b.Ignore(x => x.DomainEvents);
    }
}
