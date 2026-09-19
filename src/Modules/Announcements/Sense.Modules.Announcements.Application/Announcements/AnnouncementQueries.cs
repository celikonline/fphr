using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Announcements;

/// <summary>Duyurular feed: published announcements, pinned first, then newest first.</summary>
[RequiresPermission("announcements.feed.view")]
public sealed record GetAnnouncementFeedQuery(AnnouncementCategory? Category, PagedQuery Paging) : IQuery<PagedResult<AnnouncementDto>>;

public sealed class GetAnnouncementFeedHandler(IAnnouncementRepository announcements)
    : IQueryHandler<GetAnnouncementFeedQuery, PagedResult<AnnouncementDto>>
{
    public async Task<Result<PagedResult<AnnouncementDto>>> Handle(GetAnnouncementFeedQuery query, CancellationToken cancellationToken)
    {
        var total = await announcements.CountFeedAsync(query.Category, cancellationToken);
        var list = await announcements.ListFeedAsync(query.Category, query.Paging.Skip, query.Paging.PageSize, cancellationToken);
        var items = list.Select(AnnouncementMapper.ToDto).ToList();
        return new PagedResult<AnnouncementDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }
}

/// <summary>HR management list: every status (drafts and archived included), newest created first.</summary>
[RequiresPermission("announcements.posts.manage")]
public sealed record ListAnnouncementsQuery(AnnouncementStatus? Status, PagedQuery Paging) : IQuery<PagedResult<AnnouncementDto>>;

public sealed class ListAnnouncementsHandler(IAnnouncementRepository announcements)
    : IQueryHandler<ListAnnouncementsQuery, PagedResult<AnnouncementDto>>
{
    public async Task<Result<PagedResult<AnnouncementDto>>> Handle(ListAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        var total = await announcements.CountAllAsync(query.Status, cancellationToken);
        var list = await announcements.ListAllAsync(query.Status, query.Paging.Skip, query.Paging.PageSize, cancellationToken);
        var items = list.Select(AnnouncementMapper.ToDto).ToList();
        return new PagedResult<AnnouncementDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }
}

/// <summary>Reader detail - only Published announcements are visible here (drafts/archived return not found).</summary>
[RequiresPermission("announcements.feed.view")]
public sealed record GetAnnouncementQuery(Guid AnnouncementId) : IQuery<AnnouncementDto>;

public sealed class GetAnnouncementHandler(IAnnouncementRepository announcements) : IQueryHandler<GetAnnouncementQuery, AnnouncementDto>
{
    public async Task<Result<AnnouncementDto>> Handle(GetAnnouncementQuery query, CancellationToken cancellationToken)
    {
        var existing = await announcements.GetByIdAsync(query.AnnouncementId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        return existing.Value.Status == AnnouncementStatus.Published
            ? AnnouncementMapper.ToDto(existing.Value)
            : Error.NotFound(AnnouncementsDomainErrors.NotFound);
    }
}

/// <summary>
/// Home "Etkinlik takvimi" widget: published Event announcements starting within the next <see cref="Days"/> days
/// (default 30, max 365), plus events already running now; soonest first.
/// </summary>
[RequiresPermission("announcements.feed.view")]
public sealed record GetUpcomingEventsQuery(int? Days, int? Limit) : IQuery<IReadOnlyList<UpcomingEventDto>>;

public sealed class GetUpcomingEventsHandler(IAnnouncementRepository announcements, TimeProvider clock)
    : IQueryHandler<GetUpcomingEventsQuery, IReadOnlyList<UpcomingEventDto>>
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public async Task<Result<IReadOnlyList<UpcomingEventDto>>> Handle(GetUpcomingEventsQuery query, CancellationToken cancellationToken)
    {
        var days = Math.Clamp(query.Days ?? AnnouncementsLimits.UpcomingEventsDefaultDays, 1, AnnouncementsLimits.UpcomingEventsMaxDays);
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var now = clock.GetUtcNow().UtcDateTime;

        var list = await announcements.ListUpcomingEventsAsync(now, now.AddDays(days), limit, cancellationToken);
        IReadOnlyList<UpcomingEventDto> dtos = list.Select(AnnouncementMapper.ToUpcomingEvent).ToList();
        return Result.Success(dtos);
    }
}
