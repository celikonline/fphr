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

/// <summary>
/// One row in a company's "diğer açık pozisyonlar" (other open positions) list, or in the tenant-wide public
/// posting list (S-10, <c>GET recruitment/public/postings?tenant=</c>). <c>Location</c> is always null for now:
/// <see cref="Sense.Modules.Organization.Contracts.ILocationLookup"/> only exposes
/// <c>ExistsActiveAsync</c> (a boolean), not a display name, so there's no cross-module port yet to resolve
/// <see cref="Sense.Modules.Recruitment.Domain.JobPostings.JobPosting.LocationId"/> to a string - revisit once
/// Organization.Contracts grows a name-returning lookup.
/// </summary>
public sealed record PublicJobPostingSummaryDto(string ApplySlug, string Title, string? Location, string EmploymentType, DateTime? PublishedAt);
