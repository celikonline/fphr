using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain.DocumentTypes;

public enum DocumentRequirementStatus
{
    Missing = 0,
    Uploaded = 1,
    Waived = 2,
}

/// <summary>One row per (TenantId, EmployeeId, DocumentTypeId): tracks whether an applicable document type has been
/// satisfied for a given employee.</summary>
public sealed class EmployeeDocumentRequirement : TenantAggregateRoot<Guid>, IAuditLogged
{
    private EmployeeDocumentRequirement()
    {
    }

    private EmployeeDocumentRequirement(Guid id, Guid tenantId, Guid employeeId, Guid documentTypeId, DateOnly? dueDate, DateTime evaluatedAt)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        DocumentTypeId = documentTypeId;
        DueDate = dueDate;
        Status = DocumentRequirementStatus.Missing;
        EvaluatedAt = evaluatedAt;
    }

    public Guid EmployeeId { get; private set; }

    public Guid DocumentTypeId { get; private set; }

    public DocumentRequirementStatus Status { get; private set; }

    public DateOnly? DueDate { get; private set; }

    /// <summary>Links to the Document aggregate once satisfied.</summary>
    public Guid? DocumentId { get; private set; }

    public string? WaivedReason { get; private set; }

    public Guid? WaivedBy { get; private set; }

    public DateTime? WaivedAt { get; private set; }

    public DateTime EvaluatedAt { get; private set; }

    public static Result<EmployeeDocumentRequirement> Materialize(Guid tenantId, Guid employeeId, Guid documentTypeId, DateOnly? dueDate) =>
        new EmployeeDocumentRequirement(Guid.CreateVersion7(), tenantId, employeeId, documentTypeId, dueDate, DateTime.UtcNow);

    /// <summary>Marks the requirement as satisfied; clears any previous waive.</summary>
    public void MarkUploaded(Guid documentId)
    {
        Status = DocumentRequirementStatus.Uploaded;
        DocumentId = documentId;
        WaivedReason = null;
        WaivedBy = null;
        WaivedAt = null;
    }

    public Result Waive(string reason, Guid waivedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation(DocumentsDomainErrors.EmployeeDocumentRequirementWaiveReasonRequired);
        }

        Status = DocumentRequirementStatus.Waived;
        WaivedReason = reason.Trim();
        WaivedBy = waivedBy;
        WaivedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Admin correction path: back to Missing, clears waive fields. Leaves DocumentId/Uploaded status alone (waive only ever applied on top of Missing/Waived).</summary>
    public void ClearWaive()
    {
        Status = DocumentRequirementStatus.Missing;
        WaivedReason = null;
        WaivedBy = null;
        WaivedAt = null;
    }
}
