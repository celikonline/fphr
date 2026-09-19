using System.Globalization;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;

namespace Sense.Modules.Announcements.Application.Support;

public sealed record HelpCategoryDto(Guid Id, string Slug, string Name, string Locale, int SortOrder, int ArticleCount);

/// <summary>Search result / list row (no body).</summary>
public sealed record HelpArticleSummaryDto(
    Guid Id, string Slug, string Title, string Summary, Guid CategoryId, string Locale, IReadOnlyList<string> Tags, bool HasVideo);

public sealed record HelpArticleDto(
    Guid Id, string Slug, string Title, string Summary, string Body, Guid CategoryId, string Locale,
    IReadOnlyList<string> Tags, string? VideoUrl, IReadOnlyList<string> RelatedRoutes, DateTime? PublishedAt);

public sealed record SupportTicketDto(
    Guid Id, string Subject, string Body, SupportTicketCategory Category, SupportTicketStatus Status, SupportTicketChannel Channel,
    string? PageContext, DateTime CreatedAt);

/// <summary>Contact info for the Destek page (from SupportOptions; unset values are null and omitted from JSON).</summary>
public sealed record SupportInfoDto(string? ContactEmail, string? HelpVideoChannelUrl);

internal static class SupportMapper
{
    public static HelpArticleSummaryDto ToSummary(HelpArticle a) =>
        new(a.Id, a.Slug, a.Title, a.Summary, a.CategoryId, a.Locale, a.Tags, !string.IsNullOrWhiteSpace(a.VideoUrl));

    public static HelpArticleDto ToDto(HelpArticle a) =>
        new(a.Id, a.Slug, a.Title, a.Summary, a.Body, a.CategoryId, a.Locale, a.Tags, a.VideoUrl, a.RelatedRoutes, a.PublishedAt);

    public static SupportTicketDto ToDto(SupportTicket t) =>
        new(t.Id, t.Subject, t.Body, t.Category, t.Status, t.Channel, t.PageContext, t.CreatedAt);
}

internal static class HelpLocaleResolver
{
    /// <summary>Requested locale ("en", "en-US") if supported, else the request UI culture, else "tr".</summary>
    public static string Resolve(string? requested) =>
        HelpLocales.Normalize(requested)
        ?? HelpLocales.Normalize(CultureInfo.CurrentUICulture.Name)
        ?? HelpLocales.Default;
}
