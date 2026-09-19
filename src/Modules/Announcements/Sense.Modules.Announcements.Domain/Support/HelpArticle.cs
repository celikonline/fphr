using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain.Support;

public enum HelpArticleStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

/// <summary>
/// Help Center article (11.3.13 "Yardım makalesi", trimmed: no Media[]/Version, Body is plain text/markdown-lite
/// instead of BodyHtml). **Platform-scoped**: no TenantId, so every tenant reads the same published articles.
/// Slug is globally unique (tr and en articles use their own localized slugs). Seeded via EF HasData; there is no
/// tenant-facing editor (platform console is Faz 8).
/// </summary>
public sealed class HelpArticle : Entity<Guid>
{
    private HelpArticle()
    {
    }

    private HelpArticle(Guid id, string slug, string locale, Guid categoryId)
        : base(id)
    {
        Slug = slug;
        Locale = locale;
        CategoryId = categoryId;
        Status = HelpArticleStatus.Draft;
    }

    public string Slug { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public Guid CategoryId { get; private set; }

    public List<string> Tags { get; private set; } = [];

    /// <summary>Content locale: "tr" or "en".</summary>
    public string Locale { get; private set; } = HelpLocales.Default;

    /// <summary>Optional embedded/linked video (YouTube etc.); seeded articles leave it empty.</summary>
    public string? VideoUrl { get; private set; }

    /// <summary>App routes this article explains (e.g. "/admin/settings/import"), for page-context help.</summary>
    public List<string> RelatedRoutes { get; private set; } = [];

    public HelpArticleStatus Status { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    /// <summary>Creates a Draft article (used by tests and a future platform editor; production seed uses HasData).</summary>
    public static Result<HelpArticle> Create(
        string slug, string locale, Guid categoryId, string title, string summary, string body,
        IEnumerable<string>? tags = null, IEnumerable<string>? relatedRoutes = null, string? videoUrl = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Error.Validation(AnnouncementsDomainErrors.HelpArticleSlugRequired);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation(AnnouncementsDomainErrors.HelpArticleTitleRequired);
        }

        var normalizedLocale = HelpLocales.Normalize(locale);
        if (normalizedLocale is null)
        {
            return Error.Validation(AnnouncementsDomainErrors.HelpArticleLocaleInvalid);
        }

        return new HelpArticle(Guid.CreateVersion7(), Guard.MaxLength(slug.Trim(), AnnouncementsLimits.SlugMaxLength), normalizedLocale, categoryId)
        {
            Title = Guard.MaxLength(title.Trim(), AnnouncementsLimits.TitleMaxLength),
            Summary = Guard.MaxLength(summary.Trim(), AnnouncementsLimits.SummaryMaxLength),
            Body = Guard.MaxLength(body.Trim(), AnnouncementsLimits.HelpBodyMaxLength),
            Tags = Clean(tags),
            RelatedRoutes = Clean(relatedRoutes),
            VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl.Trim(),
        };
    }

    /// <summary>Draft/Archived -> Published.</summary>
    public Result Publish(DateTime publishedAtUtc)
    {
        Status = HelpArticleStatus.Published;
        PublishedAt = publishedAtUtc;
        return Result.Success();
    }

    public Result Archive()
    {
        Status = HelpArticleStatus.Archived;
        return Result.Success();
    }

    private static List<string> Clean(IEnumerable<string>? values) =>
        values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList() ?? [];
}
