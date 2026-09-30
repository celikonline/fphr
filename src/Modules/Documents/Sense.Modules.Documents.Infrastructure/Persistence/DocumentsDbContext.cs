using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Documents.Infrastructure.Persistence.Visa;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

/// <summary>Documents module DbContext with file document entities.</summary>
public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext), IDocumentsUnitOfWork
{
    public const string SchemaName = "documents";

    public override string Schema => SchemaName;

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentTypeDefinition> DocumentTypeDefinitions => Set<DocumentTypeDefinition>();

    public DbSet<EmployeeDocumentRequirement> EmployeeDocumentRequirements => Set<EmployeeDocumentRequirement>();

    public DbSet<VisaDocumentProcess> VisaDocumentProcesses => Set<VisaDocumentProcess>();

    public DbSet<VisaDocumentRequest> VisaDocumentRequests => Set<VisaDocumentRequest>();

    /// <summary>Shared company files (senseik-028): tenant-wide/role-restricted files, separate from the
    /// employee-scoped <see cref="Documents"/> table.</summary>
    public DbSet<SharedCompanyFile> SharedCompanyFiles => Set<SharedCompanyFile>();

    public DbSet<SharedFileType> SharedFileTypes => Set<SharedFileType>();

    public DbSet<VisaSettings> VisaSettings => Set<VisaSettings>();

    /// <summary>Shared AuditLogEntry type in this module's own schema (same pattern as Leave/Expense).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        base.OnModelCreating(modelBuilder);

        // Visa documents configurations
        modelBuilder.ApplyConfiguration(new VisaDocumentProcessConfiguration());
        modelBuilder.ApplyConfiguration(new VisaDocumentRequestConfiguration());
        modelBuilder.ApplyConfiguration(new SharedCompanyFileConfiguration());
        modelBuilder.ApplyConfiguration(new SharedFileTypeConfiguration());
    }
}

internal static class DocumentsTables
{
    public const string Documents = "documents";

    public const int TitleLength = 255;
    public const int FileKeyLength = 500;
    public const int ContentTypeLength = 100;
    public const int BucketNameLength = 100;
    public const int ETagLength = 256;
    public const int StatusLength = 50;
}

/// <summary>Document entity configuration.</summary>
public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable(DocumentsTables.Documents);
        b.HasKey(x => x.Id);

        // Tenant isolation index
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.FileKey }).IsUnique();
        // "expiring documents" report (GET /api/v1/documents/expiring) - AddDocumentExpiry migration, see
        // status/faz-04/MIGRATION-ISTEGI.md.
        b.HasIndex(x => new { x.TenantId, x.ExpiryDate });

        // Properties
        b.Property(x => x.Title).HasMaxLength(DocumentsTables.TitleLength).IsRequired();
        b.Property(x => x.FileKey).HasMaxLength(DocumentsTables.FileKeyLength).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(DocumentsTables.ContentTypeLength).IsRequired();
        b.Property(x => x.Size).IsRequired();
        b.Property(x => x.UploadedBy).IsRequired();
        b.Property(x => x.UploadedAt).IsRequired();
        b.Property(x => x.ETag).HasMaxLength(DocumentsTables.ETagLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(DocumentsTables.StatusLength).IsRequired();
        b.Property(x => x.DeletedAt);
        b.Property(x => x.DeletedBy);
        b.Property(x => x.BucketName).HasMaxLength(DocumentsTables.BucketNameLength).IsRequired();
        b.Property(x => x.ExpiryDate);
        b.Property(x => x.EmployeeId);
        b.Property(x => x.VisaDocumentRequestId);

        // "Employee's folder" lookups (e.g. delivered visa documents) and the visa audit trail.
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasIndex(x => x.VisaDocumentRequestId);

        // Ignore domain events (handled by outbox processor)
        b.Ignore(x => x.DomainEvents);

        // Audit trail (CreatedAt, ModifiedAt, etc. via IAuditLogged interface)
    }
}
