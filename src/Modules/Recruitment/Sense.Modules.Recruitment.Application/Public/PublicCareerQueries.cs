using Sense.Modules.Identity.Contracts;
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
            .Select(ToSummaryDto)
            .ToList();
        return Result.Success(dtos);
    }

    internal static PublicJobPostingSummaryDto ToSummaryDto(JobPosting p) =>
        new(p.ApplySlug, p.Title, null, p.EmploymentType.ToString(), p.PublishedAt);
}

/// <summary>
/// Public career site's posting list for one tenant (S-10, Faz 9 gün açılışı TALIMAT'ı madde 2): resolves the
/// tenant from a public-facing slug (never a raw tenant id) via Identity's <see cref="ITenantSlugLookup"/>, then
/// lists that tenant's Open, not-yet-expired postings. Deliberately returns an **empty list**, never 404, when the
/// slug doesn't resolve - a 404 would let a caller enumerate which tenant slugs exist (tenant sızıntısı).
/// </summary>
public sealed record ListPublicJobPostingsByTenantSlugQuery(string? TenantSlug) : IQuery<IReadOnlyList<PublicJobPostingSummaryDto>>;

public sealed class ListPublicJobPostingsByTenantSlugHandler(
    ITenantSlugLookup tenantSlugLookup, IJobPostingRepository postings, TimeProvider timeProvider)
    : IQueryHandler<ListPublicJobPostingsByTenantSlugQuery, IReadOnlyList<PublicJobPostingSummaryDto>>
{
    public async Task<Result<IReadOnlyList<PublicJobPostingSummaryDto>>> Handle(
        ListPublicJobPostingsByTenantSlugQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<PublicJobPostingSummaryDto> empty = [];
        if (string.IsNullOrWhiteSpace(query.TenantSlug))
        {
            return Result.Success(empty);
        }

        var tenantId = await tenantSlugLookup.FindTenantIdAsync(query.TenantSlug, cancellationToken);
        if (tenantId is null)
        {
            return Result.Success(empty);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var open = await postings.ListPublicByTenantAsync(tenantId.Value, today, cancellationToken);
        IReadOnlyList<PublicJobPostingSummaryDto> dtos = open.Select(ListRelatedPublicJobPostingsHandler.ToSummaryDto).ToList();
        return Result.Success(dtos);
    }
}
