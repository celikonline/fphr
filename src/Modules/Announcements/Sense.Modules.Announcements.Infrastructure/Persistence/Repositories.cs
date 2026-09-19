using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

public sealed class AnnouncementRepository(AnnouncementsDbContext context) : IAnnouncementRepository
{
    public void Add(Announcement announcement) => context.Announcements.Add(announcement);

    public async Task<Result<Announcement>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var announcement = await context.Announcements.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return announcement is null ? Error.NotFound(AnnouncementsDomainErrors.NotFound) : announcement;
    }

    public async Task<IReadOnlyList<Announcement>> ListFeedAsync(
        AnnouncementCategory? category, int skip, int take, CancellationToken cancellationToken = default) =>
        await Feed(category)
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PublishedAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountFeedAsync(AnnouncementCategory? category, CancellationToken cancellationToken = default) =>
        await Feed(category).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Announcement>> ListAllAsync(
        AnnouncementStatus? status, int skip, int take, CancellationToken cancellationToken = default) =>
        await All(status)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAllAsync(AnnouncementStatus? status, CancellationToken cancellationToken = default) =>
        await All(status).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Announcement>> ListUpcomingEventsAsync(
        DateTime fromUtc, DateTime toUtc, int take, CancellationToken cancellationToken = default) =>
        await context.Announcements
            .Where(a => a.Status == AnnouncementStatus.Published && a.Category == AnnouncementCategory.Event && a.EventStartsAt != null)
            .Where(a => (a.EventStartsAt >= fromUtc && a.EventStartsAt <= toUtc)
                || (a.EventStartsAt < fromUtc && a.EventEndsAt != null && a.EventEndsAt >= fromUtc))
            .OrderBy(a => a.EventStartsAt)
            .ThenBy(a => a.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    private IQueryable<Announcement> Feed(AnnouncementCategory? category)
    {
        var query = context.Announcements.Where(a => a.Status == AnnouncementStatus.Published);
        return category is { } c ? query.Where(a => a.Category == c) : query;
    }

    private IQueryable<Announcement> All(AnnouncementStatus? status) =>
        status is { } s ? context.Announcements.Where(a => a.Status == s) : context.Announcements;
}

/// <summary>Platform-scoped: HelpCategory is not ITenantEntity, so no Tenant query filter applies to these queries.</summary>
public sealed class HelpCategoryRepository(AnnouncementsDbContext context) : IHelpCategoryRepository
{
    public async Task<IReadOnlyList<HelpCategory>> ListAsync(string locale, CancellationToken cancellationToken = default) =>
        await context.HelpCategories
            .AsNoTracking()
            .Where(c => c.Locale == locale)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountPublishedArticlesAsync(string locale, CancellationToken cancellationToken = default) =>
        await context.HelpArticles
            .Where(a => a.Locale == locale && a.Status == HelpArticleStatus.Published)
            .GroupBy(a => a.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
}

/// <summary>Platform-scoped: HelpArticle is not ITenantEntity, so every tenant reads the same rows.</summary>
public sealed class HelpArticleRepository(AnnouncementsDbContext context) : IHelpArticleRepository
{
    private const int TitleRank = 0;
    private const int SummaryRank = 1;
    private const int TagRank = 2;
    private const int BodyRank = 3;

    public void Add(HelpArticle article) => context.HelpArticles.Add(article);

    public async Task<HelpArticle?> FindPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        await context.HelpArticles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Slug == slug && a.Status == HelpArticleStatus.Published, cancellationToken);

    public async Task<IReadOnlyList<HelpArticle>> SearchPublishedAsync(
        string locale, string? term, Guid? categoryId, int take, CancellationToken cancellationToken = default)
    {
        var query = context.HelpArticles
            .AsNoTracking()
            .Where(a => a.Locale == locale && a.Status == HelpArticleStatus.Published);

        if (categoryId is { } cat)
        {
            query = query.Where(a => a.CategoryId == cat);
        }

        if (string.IsNullOrWhiteSpace(term))
        {
            return await query.OrderBy(a => a.Title).Take(take).ToListAsync(cancellationToken);
        }

        // Plain ILIKE substring match (PostgreSQL's default LIKE escape character is '\'). Ranking: title > summary >
        // tag > body, then alphabetical. tsvector('turkish') + pg_trgm ranking is a noted gap (faz4-durum-takibi An0).
        var pattern = "%" + EscapeLike(term.Trim()) + "%";
        return await query
            .Where(a => EF.Functions.ILike(a.Title, pattern)
                || EF.Functions.ILike(a.Summary, pattern)
                || a.Tags.Any(t => EF.Functions.ILike(t, pattern))
                || EF.Functions.ILike(a.Body, pattern))
            .OrderBy(a => EF.Functions.ILike(a.Title, pattern) ? TitleRank
                : EF.Functions.ILike(a.Summary, pattern) ? SummaryRank
                : a.Tags.Any(t => EF.Functions.ILike(t, pattern)) ? TagRank
                : BodyRank)
            .ThenBy(a => a.Title)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}

public sealed class SupportTicketRepository(AnnouncementsDbContext context) : ISupportTicketRepository
{
    public void Add(SupportTicket ticket) => context.SupportTickets.Add(ticket);

    public async Task<IReadOnlyList<SupportTicket>> ListByUserAsync(Guid userId, int skip, int take, CancellationToken cancellationToken = default) =>
        await context.SupportTickets
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.SupportTickets.CountAsync(t => t.UserId == userId, cancellationToken);
}
