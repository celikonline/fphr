using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Assets.Application;
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

    public const int CodeLength = 100;
    public const int NameLength = 200;
    public const int AssetNameLength = 255;
    public const int SerialNumberLength = 100;
    public const int DescriptionLength = 1000;
    public const int StatusLength = 30;
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

        b.HasIndex(x => new { x.TenantId, x.CategoryId });
        b.HasIndex(x => new { x.TenantId, x.ApprovalRequestId });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.AssignedEmployeeId });

        b.Ignore(x => x.DomainEvents);
    }
}
