namespace Sense.Modules.Recruitment.Application.JobPostings;

/// <summary>Read model for a <see cref="Sense.Modules.Recruitment.Domain.JobPostings.JobPosting"/>.</summary>
public sealed record JobPostingDto(
    Guid Id,
    string Title,
    Guid OrgUnitId,
    Guid? LocationId,
    string EmploymentType,
    string? Description,
    string? CompanyNotes,
    Guid OwnerUserId,
    Guid? HiringManagerUserId,
    Guid? RequisitionId,
    string Status,
    string ApplySlug,
    DateOnly? ClosingDate,
    DateTime? PublishedAt,
    DateTime? ArchivedAt,
    string? ArchiveReason,
    IReadOnlyList<Guid> AccessUserIds);
