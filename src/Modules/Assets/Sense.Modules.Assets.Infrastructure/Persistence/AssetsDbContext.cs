using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Assets.Infrastructure.Persistence;

/// <summary>Assets module DbContext (ADR 0003: one DbContext per module, own PostgreSQL schema "assets").</summary>
public sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IAssetsUnitOfWork
{
    public const string SchemaName = "assets";

    public override string Schema => SchemaName;

    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();

    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();

    /// <summary>Shared AuditLogEntry type in this module's own schema (same pattern as Leave/Expense).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>AuditLogEntryConfiguration must be applied BEFORE the base call (see LeaveDbContext.OnModelCreating).</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}

internal static class AssetsTables
{
    public const string AssetCategories = "asset_categories";
    public const string Assets = "assets";
    public const string AssetAssignments = "asset_assignments";

    public const int CodeLength = 100;
    public const int NameLength = 200;
    public const int AssetNameLength = 255;
    public const int SerialNumberLength = 100;
    public const int DescriptionLength = 1000;
    public const int StatusLength = 30;
    public const int AssetTagLength = 50;
    public const int BrandLength = 100;
    public const int ModelLength = 100;
    public const int NoteLength = 500;
    public const int ConditionLength = 20;
    public const int EndReasonLength = 20;
}

public sealed class AssetCategoryConfiguration : IEntityTypeConfiguration<AssetCategory>
{
    public void Configure(EntityTypeBuilder<AssetCategory> b)
    {
        b.ToTable(AssetsTables.AssetCategories);
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(AssetsTables.CodeLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(AssetsTables.NameLength).IsRequired();
        b.Ignore(x => x.RequiresPeriodicReplacement);

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> b)
    {
        b.ToTable(AssetsTables.Assets);
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(AssetsTables.AssetNameLength).IsRequired();
        b.Property(x => x.SerialNumber).HasMaxLength(AssetsTables.SerialNumberLength);
        b.Property(x => x.Description).HasMaxLength(AssetsTables.DescriptionLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(AssetsTables.StatusLength).IsRequired();
        b.Property(x => x.AssetTag).HasMaxLength(AssetsTables.AssetTagLength);
        b.Property(x => x.Brand).HasMaxLength(AssetsTables.BrandLength);
        b.Property(x => x.Model).HasMaxLength(AssetsTables.ModelLength);
        b.Property(x => x.PurchaseCost).HasPrecision(AssetsLimits.CostPrecision, AssetsLimits.CostScale);
        b.Property(x => x.Condition).HasConversion<string>().HasMaxLength(AssetsTables.ConditionLength).IsRequired();

        b.HasMany(x => x.Assignments).WithOne().HasForeignKey(a => a.AssetId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.OpenAssignment);

        b.HasIndex(x => new { x.TenantId, x.CategoryId });
        b.HasIndex(x => new { x.TenantId, x.ApprovalRequestId });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.AssignedEmployeeId });

        // Demirbaş no is unique per tenant. The index is a case-sensitive backstop on the trimmed stored value; the
        // case-insensitive duplicate check ("ABC-1" vs "abc-1") is done by the handlers via IsAssetTagTakenAsync
        // (lower() on both sides) rather than a lower(asset_tag) expression index, which EF cannot model.
        b.HasIndex(x => new { x.TenantId, x.AssetTag }).IsUnique().HasFilter("asset_tag IS NOT NULL");

        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
{
    public void Configure(EntityTypeBuilder<AssetAssignment> b)
    {
        b.ToTable(AssetsTables.AssetAssignments);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.EndReason).HasConversion<string>().HasMaxLength(AssetsTables.EndReasonLength);
        b.Property(x => x.ReturnCondition).HasConversion<string>().HasMaxLength(AssetsTables.ConditionLength);
        b.Property(x => x.Note).HasMaxLength(AssetsTables.NoteLength);
        b.Ignore(x => x.IsOpen);

        b.HasIndex(x => new { x.TenantId, x.AssetId });
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });

        // An asset has at most one open (unreturned) assignment.
        b.HasIndex(x => new { x.TenantId, x.AssetId }, "ux_asset_assignments_tenant_id_asset_id_open")
            .IsUnique()
            .HasFilter("returned_at IS NULL");
    }
}
