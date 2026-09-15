using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.DataTransfer.Infrastructure.Persistence;

/// <summary>DataTransfer module DbContext (schema "datatransfer"): import jobs, row outcomes and uploaded source files (bytea).</summary>
public sealed class DataTransferDbContext(DbContextOptions<DataTransferDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "datatransfer";

    public override string Schema => SchemaName;

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    public DbSet<ImportJobRow> ImportJobRows => Set<ImportJobRow>();

    public DbSet<ImportSourceFile> ImportSourceFiles => Set<ImportSourceFile>();
}

internal static class DataTransferTables
{
    public const string ImportJobs = "import_jobs";
    public const string ImportJobRows = "import_job_rows";
    public const string ImportSourceFiles = "import_source_files";
    public const int EnumLength = 30;
    public const int NameLength = 201;
    public const string ByteaColumnType = "bytea";
}

/// <summary>jsonb value conversion for small immutable lists stored on the row (column mapping, source columns, row errors).</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<IReadOnlyList<T>> AsJsonb<T>(this PropertyBuilder<IReadOnlyList<T>> property)
    {
        var converter = new ValueConverter<IReadOnlyList<T>, string>(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<List<T>>(s, Options) ?? new List<T>());
        var comparer = new ValueComparer<IReadOnlyList<T>>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => v);
        property.HasConversion(converter, comparer).HasColumnType(PersistenceDefaults.JsonbColumnType).IsRequired();
        return property;
    }
}

public sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable(DataTransferTables.ImportJobs);
        b.HasKey(x => x.Id);
        b.Property(x => x.TemplateCode).HasMaxLength(ImportJobLimits.TemplateCodeMaxLength).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(ImportJobLimits.FileNameMaxLength).IsRequired();
        b.Property(x => x.Format).HasConversion<string>().HasMaxLength(DataTransferTables.EnumLength).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(DataTransferTables.EnumLength).IsRequired();
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(DataTransferTables.EnumLength).IsRequired();
        b.Property(x => x.FailurePolicy).HasConversion<string>().HasMaxLength(DataTransferTables.EnumLength).IsRequired();
        b.Property(x => x.RequestedByName).HasMaxLength(DataTransferTables.NameLength);
        b.Property(x => x.FailureCode).HasMaxLength(ImportJobLimits.CodeMaxLength);
        b.Property(x => x.SourceColumns).AsJsonb();
        b.Property(x => x.Mapping).AsJsonb();
        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Ignore(x => x.IsFinished);
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class ImportJobRowConfiguration : IEntityTypeConfiguration<ImportJobRow>
{
    public void Configure(EntityTypeBuilder<ImportJobRow> b)
    {
        b.ToTable(DataTransferTables.ImportJobRows);
        b.HasKey(x => x.Id);
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(DataTransferTables.EnumLength).IsRequired();
        b.Property(x => x.MatchKey).HasMaxLength(ImportJobLimits.MatchKeyMaxLength);
        b.Property(x => x.Errors).AsJsonb();
        b.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.ImportJobId, x.Outcome });
        b.HasIndex(x => new { x.ImportJobId, x.RowNumber });
    }
}

public sealed class ImportSourceFileConfiguration : IEntityTypeConfiguration<ImportSourceFile>
{
    public void Configure(EntityTypeBuilder<ImportSourceFile> b)
    {
        b.ToTable(DataTransferTables.ImportSourceFiles);
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(ImportJobLimits.FileNameMaxLength).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(ImportJobLimits.ContentTypeMaxLength).IsRequired();
        b.Property(x => x.Content).HasColumnType(DataTransferTables.ByteaColumnType).IsRequired();
        b.HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.ImportJobId).IsUnique();
    }
}
