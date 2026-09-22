using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.OccupationalSafety.Application;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;
using Sense.Modules.OccupationalSafety.Domain.Services;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

using Sense.Modules.OccupationalSafety.Domain.Incidents;

namespace Sense.Modules.OccupationalSafety.Infrastructure.Persistence;

/// <summary>Occupational Safety (İSG) module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "ohs").</summary>
public sealed class OhsDbContext(DbContextOptions<OhsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IOhsUnitOfWork
{
    public const string SchemaName = "ohs";

    public override string Schema => SchemaName;

    public DbSet<WorkplaceHazardClassification> HazardClassifications => Set<WorkplaceHazardClassification>();

    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();

    public DbSet<OhsIncident> Incidents => Set<OhsIncident>();

    public DbSet<SafetyAssignment> SafetyAssignments => Set<SafetyAssignment>();

    public DbSet<OhsCommitteeMember> CommitteeMembers => Set<OhsCommitteeMember>();

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
    public const string Incidents = "incidents";
    public const string SafetyAssignments = "safety_assignments";
    public const string CommitteeMembers = "committee_members";
    public const int ServiceEnumLength = 40;
    public const int IncidentKindLength = 30;
    public const int IncidentTextLength = 2000;

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

public sealed class OhsIncidentConfiguration : IEntityTypeConfiguration<OhsIncident>
{
    public void Configure(EntityTypeBuilder<OhsIncident> b)
    {
        b.ToTable(OhsTables.Incidents);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(OhsTables.IncidentKindLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(OhsTables.IncidentTextLength).IsRequired();
        b.Property(x => x.CorrectiveAction).HasMaxLength(OhsTables.IncidentTextLength);
        b.Ignore(x => x.IsClosed);
        b.HasIndex(x => new { x.TenantId, x.EventDate });
        b.HasIndex(x => new { x.TenantId, x.ReportedByEmployeeId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class SafetyAssignmentConfiguration : IEntityTypeConfiguration<SafetyAssignment>
{
    public void Configure(EntityTypeBuilder<SafetyAssignment> b)
    {
        b.ToTable(OhsTables.SafetyAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(OhsTables.ServiceEnumLength).IsRequired();
        b.Property(x => x.CertificateClass).HasConversion<string>().HasMaxLength(OhsTables.ServiceEnumLength);
        b.Property(x => x.PersonName).HasMaxLength(OhsLimits.PersonNameMaxLength).IsRequired();
        b.Property(x => x.CertificateNumber).HasMaxLength(OhsLimits.PersonNameMaxLength);
        b.Property(x => x.ProviderName).HasMaxLength(OhsLimits.PersonNameMaxLength);
        b.HasIndex(x => new { x.TenantId, x.LocationId });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class OhsCommitteeMemberConfiguration : IEntityTypeConfiguration<OhsCommitteeMember>
{
    public void Configure(EntityTypeBuilder<OhsCommitteeMember> b)
    {
        b.ToTable(OhsTables.CommitteeMembers);
        b.HasKey(x => x.Id);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(OhsTables.ServiceEnumLength).IsRequired();
        b.Property(x => x.PersonName).HasMaxLength(OhsLimits.PersonNameMaxLength).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.LocationId });
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
