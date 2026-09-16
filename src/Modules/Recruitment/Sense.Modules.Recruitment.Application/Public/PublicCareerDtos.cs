namespace Sense.Modules.Recruitment.Application.Public;

/// <summary>
/// Public career-site read model for a single Open job posting (R3). Deliberately narrower than
/// <see cref="Sense.Modules.Recruitment.Application.JobPostings.JobPostingDto"/>: no internal ids
/// (OwnerUserId, HiringManagerUserId, RequisitionId, AccessUserIds) leak to an anonymous caller.
/// <c>Location</c> is resolved via <see cref="Sense.Modules.Organization.Contracts.ILocationLookup.GetNamesAsync"/>
/// (S-19) - null when the posting has no <see cref="Sense.Modules.Recruitment.Domain.JobPostings.JobPosting.LocationId"/>
/// or the lookup has no name for it (e.g. anonymous request context, see handler notes).
/// </summary>
public sealed record PublicJobPostingDto(
    string ApplySlug,
    string Title,
    string EmploymentType,
    string? Description,
    DateOnly? ClosingDate,
    DateTime? PublishedAt,
    string? Location = null);

/// <summary>
/// One row in a company's "diğer açık pozisyonlar" (other open positions) list, or in the tenant-wide public
/// posting list (S-10, <c>GET recruitment/public/postings?tenant=</c>). <c>Location</c> is resolved via
/// <see cref="Sense.Modules.Organization.Contracts.ILocationLookup.GetNamesAsync"/> (S-19) - null when the
/// posting has no <see cref="Sense.Modules.Recruitment.Domain.JobPostings.JobPosting.LocationId"/> or the
/// lookup has no name for it.
/// </summary>
public sealed record PublicJobPostingSummaryDto(string ApplySlug, string Title, string? Location, string EmploymentType, DateTime? PublishedAt);
