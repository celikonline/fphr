using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain;

/// <summary>Announcement aggregate repository (tenant-filtered).</summary>
public interface IAnnouncementRepository
{
    void Add(Announcement announcement);

    Task<Result<Announcement>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Published announcements: pinned first, then newest PublishedAt first.</summary>
    Task<IReadOnlyList<Announcement>> ListFeedAsync(AnnouncementCategory? category, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountFeedAsync(AnnouncementCategory? category, CancellationToken cancellationToken = default);

    /// <summary>All statuses (HR management list), newest created first.</summary>
    Task<IReadOnlyList<Announcement>> ListAllAsync(AnnouncementStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAllAsync(AnnouncementStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Published Event announcements that start within [fromUtc, toUtc], plus ones already running at fromUtc
    /// (started earlier, EventEndsAt &gt;= fromUtc); ordered by EventStartsAt ascending.
    /// </summary>
    Task<IReadOnlyList<Announcement>> ListUpcomingEventsAsync(DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default);
}

/// <summary>Platform-scoped Help Center categories (no tenant filter).</summary>
public interface IHelpCategoryRepository
{
    Task<IReadOnlyList<HelpCategory>> ListAsync(string locale, CancellationToken cancellationToken = default);

    /// <summary>Published article count per category id, for the given locale.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPublishedArticlesAsync(string locale, CancellationToken cancellationToken = default);
}

/// <summary>Platform-scoped Help Center articles (no tenant filter).</summary>
public interface IHelpArticleRepository
{
    void Add(HelpArticle article);

    /// <summary>Published article by globally unique slug (any locale).</summary>
    Task<HelpArticle?> FindPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Published articles in <paramref name="locale"/>. With a term: keyword match over Title/Summary/Tags/Body
    /// (case-insensitive ILIKE), ranked title &gt; summary &gt; tag &gt; body match, then by title. Without a term:
    /// all published articles (optionally one category) ordered by title.
    /// </summary>
    Task<IReadOnlyList<HelpArticle>> SearchPublishedAsync(string locale, string? term, Guid? categoryId, int take, CancellationToken cancellationToken = default);
}

/// <summary>Support tickets (tenant-filtered).</summary>
public interface ISupportTicketRepository
{
    void Add(SupportTicket ticket);

    Task<IReadOnlyList<SupportTicket>> ListByUserAsync(Guid userId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
