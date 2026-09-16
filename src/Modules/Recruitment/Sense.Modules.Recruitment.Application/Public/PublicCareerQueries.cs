using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Contracts;
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

public sealed class GetPublicJobPostingHandler(IJobPostingRepository postings, ILocationLookup locations, TimeProvider timeProvider)
    : IQueryHandler<GetPublicJobPostingQuery, PublicJobPostingDto>
{
    public async Task<Result<PublicJobPostingDto>> Handle(GetPublicJobPostingQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var found = await postings.GetPublicByApplySlugAsync(query.ApplySlug, today, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var posting = found.Value;
        string? location = null;
        if (posting.LocationId is { } locationId)
        {
            var names = await locations.GetNamesAsync(posting.TenantId, [locationId], cancellationToken);
            names.TryGetValue(locationId, out location);
        }

        return ToDto(posting, location);
    }

    internal static PublicJobPostingDto ToDto(JobPosting posting, string? location = null) =>
        new(posting.ApplySlug, posting.Title, posting.EmploymentType.ToString(), posting.Description, posting.ClosingDate, posting.PublishedAt, location);
}

/// <summary>Other Open postings of the same company (R3 "diğer açık pozisyonlar"), reached by anchoring on one posting's slug.</summary>
public sealed record ListRelatedPublicJobPostingsQuery(string ApplySlug) : IQuery<IReadOnlyList<PublicJobPostingSummaryDto>>;

public sealed class ListRelatedPublicJobPostingsHandler(IJobPostingRepository postings, ILocationLookup locations, TimeProvider timeProvider)
    : IQueryHandler<ListRelatedPublicJobPostingsQuery, IReadOnlyList<PublicJobPostingSummaryDto>>
{
    public async Task<Result<IReadOnlyList<PublicJobPostingSummaryDto>>> Handle(
        ListRelatedPublicJobPostingsQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var anchor = await postings.GetPublicByApplySlugAsync(query.ApplySlug, today, cancellationToken);
        if (anchor.IsFailure)
        {
            return anchor.Error;
        }

        var related = await postings.ListPublicOpenAsync(anchor.Value.TenantId, anchor.Value.Id, today, cancellationToken);
        var locationNames = await ResolveLocationNamesAsync(anchor.Value.TenantId, related, locations, cancellationToken);
        IReadOnlyList<PublicJobPostingSummaryDto> dtos = related
            .Select(p => ToSummaryDto(p, locationNames))
            .ToList();
        return Result.Success(dtos);
    }

    /// <summary>Resolves the distinct, non-null <c>LocationId</c>s of a posting batch in a single lookup call (no N+1).</summary>
    internal static async Task<IReadOnlyDictionary<Guid, string>> ResolveLocationNamesAsync(
        Guid tenantId, IReadOnlyCollection<JobPosting> postingBatch, ILocationLookup locations, CancellationToken cancellationToken)
    {
        var locationIds = postingBatch
            .Where(p => p.LocationId is not null)
            .Select(p => p.LocationId!.Value)
            .Distinct()
            .ToList();
        return locationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await locations.GetNamesAsync(tenantId, locationIds, cancellationToken);
    }

    internal static PublicJobPostingSummaryDto ToSummaryDto(JobPosting p, IReadOnlyDictionary<Guid, string> locationNames)
    {
        string? location = p.LocationId is { } locationId && locationNames.TryGetValue(locationId, out var name) ? name : null;
        return new(p.ApplySlug, p.Title, location, p.EmploymentType.ToString(), p.PublishedAt);
    }
}

/// <summary>
/// Public career site's posting list for one tenant (S-10, Faz 9 gün açılışı TALIMAT'ı madde 2): resolves the
/// tenant from a public-facing slug (never a raw tenant id) via Identity's <see cref="ITenantSlugLookup"/>, then
/// lists that tenant's Open, not-yet-expired postings. Deliberately returns an **empty list**, never 404, when the
/// slug doesn't resolve - a 404 would let a caller enumerate which tenant slugs exist (tenant sızıntısı).
/// </summary>
public sealed record ListPublicJobPostingsByTenantSlugQuery(string? TenantSlug) : IQuery<IReadOnlyList<PublicJobPostingSummaryDto>>;

public sealed class ListPublicJobPostingsByTenantSlugHandler(
    ITenantSlugLookup tenantSlugLookup, IJobPostingRepository postings, ILocationLookup locations, TimeProvider timeProvider)
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
        var locationNames = await ListRelatedPublicJobPostingsHandler.ResolveLocationNamesAsync(tenantId.Value, open, locations, cancellationToken);
        IReadOnlyList<PublicJobPostingSummaryDto> dtos = open
            .Select(p => ListRelatedPublicJobPostingsHandler.ToSummaryDto(p, locationNames))
            .ToList();
        return Result.Success(dtos);
    }
}
