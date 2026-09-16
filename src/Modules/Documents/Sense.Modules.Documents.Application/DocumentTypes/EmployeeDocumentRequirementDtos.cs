using Sense.Modules.Documents.Domain.DocumentTypes;

namespace Sense.Modules.Documents.Application.DocumentTypes;

public sealed record EmployeeDocumentRequirementDto(
    Guid Id,
    Guid EmployeeId,
    Guid DocumentTypeId,
    string DocumentTypeCode,
    string DocumentTypeName,
    DocumentTypeStage Stage,
    DocumentRequirementStatus Status,
    DateOnly? DueDate,
    Guid? DocumentId,
    string? WaivedReason)
{
    public static EmployeeDocumentRequirementDto From(EmployeeDocumentRequirement requirement, DocumentTypeDefinition type) => new(
        requirement.Id, requirement.EmployeeId, requirement.DocumentTypeId, type.Code, type.Name, type.Stage,
        requirement.Status, requirement.DueDate, requirement.DocumentId, requirement.WaivedReason);
}

public sealed record MissingDocumentReportItemDto(
    Guid RequirementId,
    Guid EmployeeId,
    string EmployeeName,
    string DocumentTypeCode,
    string DocumentTypeName,
    DocumentTypeStage Stage,
    DocumentRequirementStatus Status,
    DateOnly? DueDate);

/// <summary>Row for <c>GET /api/v1/documents/expiring</c> (S-6 - shared with Faz 4 FE this turn).</summary>
public sealed record ExpiringDocumentDto(
    Guid DocumentId,
    Guid EmployeeId,
    string EmployeeName,
    Guid DocumentTypeId,
    string DocumentTypeName,
    DateOnly ExpiryDate,
    int DaysLeft);
