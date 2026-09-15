using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sense.Modules.Documents.Domain.DocumentTypes;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

internal static class DocumentTypeTables
{
    public const string DocumentTypeDefinitions = "document_type_definitions";
    public const string EmployeeDocumentRequirements = "employee_document_requirements";

    public const int CodeLength = 100;
    public const int NameLength = 255;
    public const int StageLength = 50;
    public const int ConditionLength = 50;
    public const int StatusLength = 50;
    public const int WaivedReasonLength = 500;
}

/// <summary>Document type catalog entity configuration.</summary>
public sealed class DocumentTypeDefinitionConfiguration : IEntityTypeConfiguration<DocumentTypeDefinition>
{
    public void Configure(EntityTypeBuilder<DocumentTypeDefinition> b)
    {
        b.ToTable(DocumentTypeTables.DocumentTypeDefinitions);
        b.HasKey(x => x.Id);

        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Stage });

        b.Property(x => x.Code).HasMaxLength(DocumentTypeTables.CodeLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(DocumentTypeTables.NameLength).IsRequired();
        b.Property(x => x.Stage).HasConversion<string>().HasMaxLength(DocumentTypeTables.StageLength).IsRequired();
        b.Property(x => x.IsRequired).IsRequired();
        b.Property(x => x.Condition).HasConversion<string>().HasMaxLength(DocumentTypeTables.ConditionLength);
        b.Property(x => x.RenewalPeriodDays);
        b.Property(x => x.Quantity).IsRequired();
        b.Property(x => x.IsSensitive).IsRequired();
        b.Property(x => x.DefaultRetentionDays);
        b.Property(x => x.IsSystem).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();

        // Ignore domain events (handled by outbox processor)
        b.Ignore(x => x.DomainEvents);
    }
}

/// <summary>Per-employee document requirement tracker entity configuration.</summary>
public sealed class EmployeeDocumentRequirementConfiguration : IEntityTypeConfiguration<EmployeeDocumentRequirement>
{
    public void Configure(EntityTypeBuilder<EmployeeDocumentRequirement> b)
    {
        b.ToTable(DocumentTypeTables.EmployeeDocumentRequirements);
        b.HasKey(x => x.Id);

        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.DocumentTypeId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });

        b.Property(x => x.EmployeeId).IsRequired();
        b.Property(x => x.DocumentTypeId).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(DocumentTypeTables.StatusLength).IsRequired();
        b.Property(x => x.DueDate);
        b.Property(x => x.DocumentId);
        b.Property(x => x.WaivedReason).HasMaxLength(DocumentTypeTables.WaivedReasonLength);
        b.Property(x => x.WaivedBy);
        b.Property(x => x.WaivedAt);
        b.Property(x => x.EvaluatedAt).IsRequired();

        // Ignore domain events (handled by outbox processor)
        b.Ignore(x => x.DomainEvents);
    }
}
