using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Documents.Domain.SharedFiles;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

internal static class SharedCompanyFileTables
{
    public const string SharedCompanyFiles = "shared_company_files";

    public const int FileNameLength = 255;
    public const int FileKeyLength = 500;
    public const int ContentTypeLength = 100;
    public const int ETagLength = 256;
    public const int StatusLength = 50;
    public const int VisibilityLength = 50;

    /// <summary>Comma-joined role names; role codes never contain commas, so no escaping is needed. Generous
    /// length for a handful of role names per file.</summary>
    public const int AllowedRolesLength = 2000;
    public const char AllowedRolesSeparator = ',';
}

/// <summary>Shared company file entity configuration (senseik-028).</summary>
public sealed class SharedCompanyFileConfiguration : IEntityTypeConfiguration<SharedCompanyFile>
{
    public void Configure(EntityTypeBuilder<SharedCompanyFile> b)
    {
        b.ToTable(SharedCompanyFileTables.SharedCompanyFiles);
        b.HasKey(x => x.Id);

        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.FileKey }).IsUnique();

        b.Property(x => x.FileName).HasMaxLength(SharedCompanyFileTables.FileNameLength).IsRequired();
        b.Property(x => x.FileKey).HasMaxLength(SharedCompanyFileTables.FileKeyLength).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(SharedCompanyFileTables.ContentTypeLength).IsRequired();
        b.Property(x => x.Size).IsRequired();
        b.Property(x => x.UploadedBy).IsRequired();
        b.Property(x => x.UploadedAt).IsRequired();
        b.Property(x => x.ETag).HasMaxLength(SharedCompanyFileTables.ETagLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SharedCompanyFileTables.StatusLength).IsRequired();
        b.Property(x => x.DeletedAt);
        b.Property(x => x.DeletedBy);
        b.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(SharedCompanyFileTables.VisibilityLength).IsRequired();

        b.Property(x => x.TypeId);
        b.Property(x => x.VisibleOnMobile).HasDefaultValue(false).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.TypeId });

        var allowedRolesComparer = new ValueComparer<IReadOnlyList<string>>(
            (a, b2) => (a ?? Enumerable.Empty<string>()).SequenceEqual(b2 ?? Enumerable.Empty<string>()),
            v => v.Aggregate(0, (hash, role) => HashCode.Combine(hash, role.GetHashCode(StringComparison.Ordinal))),
            v => (IReadOnlyList<string>)v.ToList());

        b.Property(x => x.AllowedRoles)
            .HasConversion(
                roles => string.Join(SharedCompanyFileTables.AllowedRolesSeparator, roles),
                value => (IReadOnlyList<string>)(string.IsNullOrEmpty(value)
                    ? new List<string>()
                    : value.Split(SharedCompanyFileTables.AllowedRolesSeparator, StringSplitOptions.RemoveEmptyEntries).ToList()))
            .HasMaxLength(SharedCompanyFileTables.AllowedRolesLength)
            .Metadata.SetValueComparer(allowedRolesComparer);

        // Ignore domain events (handled by outbox processor)
        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>Shared file type catalog entity configuration (E8).</summary>
public sealed class SharedFileTypeConfiguration : IEntityTypeConfiguration<SharedFileType>
{
    public void Configure(EntityTypeBuilder<SharedFileType> b)
    {
        b.ToTable("shared_file_types");
        b.HasKey(x => x.Id);

        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();

        b.Property(x => x.Name).HasMaxLength(SharedFileType.NameMaxLength).IsRequired();
        b.Property(x => x.VisibleOnMobile).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();

        b.Ignore(x => x.DomainEvents);
    }
}
