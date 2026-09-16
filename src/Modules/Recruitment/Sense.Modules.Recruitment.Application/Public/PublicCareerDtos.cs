namespace Sense.Modules.Recruitment.Application.Public;

/// <summary>
/// Public career-site read model for a single Open job posting (R3). Deliberately narrower than
/// <see cref="Sense.Modules.Recruitment.Application.JobPostings.JobPostingDto"/>: no internal ids
/// (OwnerUserId, HiringManagerUserId, RequisitionId, AccessUserIds) leak to an anonymous caller.
/// </summary>
public sealed record PublicJobPostingDto(
    string ApplySlug,
    string Title,
    string EmploymentType,
    string? Description,
    DateOnly? ClosingDate,
    DateTime? PublishedAt);

/// <summary>One row in a company's "diğer açık pozisyonlar" (other open positions) list.</summary>
public sealed record PublicJobPostingSummaryDto(string ApplySlug, string Title, string EmploymentType, DateTime? PublishedAt);
