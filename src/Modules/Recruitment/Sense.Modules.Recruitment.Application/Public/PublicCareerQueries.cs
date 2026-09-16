using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Application.Public;

/// <summary>
/// Public career-site posting detail by its permanent apply slug (R3). Deliberately no <c>[RequiresPermission]</c>:
/// this is the one Recruitment read reachable by an anonymous candidate. See <see cref="JobPosting.ApplySlug"/> and
/// <see cref="IJobPostingRepository.GetPublicByApplySlugAsync"/> for how tenant isolation is preserved without
/// an authenticated tenant claim (URL never carries the tenant id/slug itself, only the posting's own slug).
/// </summary>
public sealed record GetPublicJobPostingQuery(string ApplySlug) : IQuery<PublicJobPostingDto>;

public sealed class GetPublicJobPostingHandler(IJobPostingRepository postings)
    : IQueryHandler<GetPublicJobPostingQuery, PublicJobPostingDto>
{
    public async Task<Result<PublicJobPostingDto>> Handle(GetPublicJobPostingQuery query, CancellationToken cancellationToken)
    {
        var found = await postings.GetPublicByApplySlugAsync(query.ApplySlug, cancellationToken);
        return found.IsFailure ? found.Error : ToDto(found.Value);
    }

    internal static PublicJobPostingDto ToDto(JobPosting posting) =>
        new(posting.ApplySlug, posting.Title, posting.EmploymentType.ToString(), posting.Description, posting.ClosingDate, posting.PublishedAt);
}

/// <summary>Other Open postings of the same company (R3 "diğer açık pozisyonlar"), reached by anchoring on one posting's slug.</summary>
public sealed record ListRelatedPublicJobPostingsQuery(string ApplySlug) : IQuery<IReadOnlyList<PublicJobPostingSummaryDto>>;

public sealed class ListRelatedPublicJobPostingsHandler(IJobPostingRepository postings)
    : IQueryHandler<ListRelatedPublicJobPostingsQuery, IReadOnlyList<PublicJobPostingSummaryDto>>
{
    public async Task<Result<IReadOnlyList<PublicJobPostingSummaryDto>>> Handle(
        ListRelatedPublicJobPostingsQuery query, CancellationToken cancellationToken)
    {
        var anchor = await postings.GetPublicByApplySlugAsync(query.ApplySlug, cancellationToken);
        if (anchor.IsFailure)
        {
            return anchor.Error;
        }

        var related = await postings.ListPublicOpenAsync(anchor.Value.TenantId, anchor.Value.Id, cancellationToken);
        IReadOnlyList<PublicJobPostingSummaryDto> dtos = related
            .Select(p => new PublicJobPostingSummaryDto(p.ApplySlug, p.Title, p.EmploymentType.ToString(), p.PublishedAt))
            .ToList();
        return Result.Success(dtos);
    }
}
