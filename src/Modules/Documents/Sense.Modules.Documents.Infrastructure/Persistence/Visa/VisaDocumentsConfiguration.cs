using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Documents.Infrastructure.Persistence.Visa;

/// <summary>Table name and column length constants for visa documents.</summary>
internal static class VisaDocumentsTables
{
    public const string VisaDocumentProcesses = "visa_document_processes";
    public const string VisaDocumentRequests = "visa_document_requests";
    public const string VisaSettings = "visa_settings";

    public const int NameLength = 255;
    public const int CountryCodeLength = 3;
    public const int VisaTypeLength = 50;
    public const int DescriptionLength = 2000;
    public const int RejectionReasonLength = 500;
    public const int StatusLength = 50;
}

/// <summary>jsonb value conversion for <see cref="VisaSettings.TemplateDocumentIds"/> (small immutable Guid list),
/// mirroring Employee's <c>JsonColumn.AsJsonb</c> for <c>CustomFieldDefinition.Options</c> - there's no shared
/// helper for this today since it's the first Guid-list column in the codebase.</summary>
internal static class VisaJsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<IReadOnlyList<Guid>> AsJsonb(this PropertyBuilder<IReadOnlyList<Guid>> property)
    {
        var converter = new ValueConverter<IReadOnlyList<Guid>, string>(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<List<Guid>>(s, Options) ?? new List<Guid>());
        var comparer = new ValueComparer<IReadOnlyList<Guid>>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => (IReadOnlyList<Guid>)new List<Guid>(v));
        property.HasConversion(converter, comparer).HasColumnType(PersistenceDefaults.JsonbColumnType).IsRequired();
        return property;
    }
}

/// <summary>VisaDocumentProcess entity configuration for EF Core.</summary>
public sealed class VisaDocumentProcessConfiguration : IEntityTypeConfiguration<VisaDocumentProcess>
{
    public void Configure(EntityTypeBuilder<VisaDocumentProcess> b)
    {
        b.ToTable(VisaDocumentsTables.VisaDocumentProcesses);
        b.HasKey(x => x.Id);

        // Tenant isolation index
        b.HasIndex(x => new { x.TenantId, x.IsActive });
        // Backstop for the lazy default seed racing on first access (the case-insensitive check lives in the handlers).
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CountryCode }).IsUnique(false);

        // Properties
        b.Property(x => x.Name).HasMaxLength(VisaDocumentsTables.NameLength).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(VisaDocumentsTables.CountryCodeLength);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.ModifiedAt);

        // Ignore domain events
        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>VisaDocumentRequest entity configuration for EF Core.</summary>
public sealed class VisaDocumentRequestConfiguration : IEntityTypeConfiguration<VisaDocumentRequest>
{
    public void Configure(EntityTypeBuilder<VisaDocumentRequest> b)
    {
        b.ToTable(VisaDocumentsTables.VisaDocumentRequests);
        b.HasKey(x => x.Id);

        // Tenant isolation index
        b.HasIndex(x => new { x.TenantId, x.EmployeeId });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.ApprovalRequestId }).IsUnique(false);

        // Properties
        b.Property(x => x.EmployeeId).IsRequired();
        b.Property(x => x.VisaDocumentProcessId);
        b.Property(x => x.TravelStart).IsRequired();
        b.Property(x => x.TravelEnd).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(VisaDocumentsTables.CountryCodeLength).IsRequired();
        b.Property(x => x.VisaType).HasMaxLength(VisaDocumentsTables.VisaTypeLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(VisaDocumentsTables.DescriptionLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(VisaDocumentsTables.StatusLength).IsRequired();
        b.Property(x => x.ApprovalRequestId);
        b.Property(x => x.RejectionReason).HasMaxLength(VisaDocumentsTables.RejectionReasonLength);
        b.Property(x => x.RejectedBy);
        b.Property(x => x.RejectedAt);
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.ApprovedAt);
        b.Property(x => x.CompletedAt);

        // Ignore domain events
        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>VisaSettings entity configuration for EF Core: one row per tenant.</summary>
public sealed class VisaSettingsConfiguration : IEntityTypeConfiguration<VisaSettings>
{
    public void Configure(EntityTypeBuilder<VisaSettings> b)
    {
        b.ToTable(VisaDocumentsTables.VisaSettings);
        b.HasKey(x => x.Id);

        // One settings row per tenant.
        b.HasIndex(x => x.TenantId).IsUnique();

        b.Property(x => x.ApproverEmployeeId);
        b.Property(x => x.TemplateDocumentIds)
            .HasField("_templateDocumentIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AsJsonb();
        b.Property(x => x.CreatedAt).IsRequired();

        b.Ignore(x => x.DomainEvents);
    }
}
