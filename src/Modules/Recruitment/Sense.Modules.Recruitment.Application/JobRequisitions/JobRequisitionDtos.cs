namespace Sense.Modules.Recruitment.Application.JobRequisitions;

/// <summary>Read model for a <see cref="Sense.Modules.Recruitment.Domain.JobRequisitions.JobRequisition"/>.</summary>
public sealed record JobRequisitionDto(
    Guid Id,
    string Title,
    Guid OrgUnitId,
    string EmploymentType,
    int HeadcountRequested,
    string Reason,
    string? Justification,
    Guid RequesterEmployeeId,
    string Status,
    Guid? ApprovalRequestId,
    DateTime? DecidedAt);
