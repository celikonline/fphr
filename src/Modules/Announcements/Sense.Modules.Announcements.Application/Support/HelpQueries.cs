using Microsoft.Extensions.Options;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Support;

/// <summary>Help Center categories for the caller's locale (platform content - identical for every tenant).</summary>
[RequiresPermission("announcements.help.view")]
public sealed record ListHelpCategoriesQuery(string? Locale) : IQuery<IReadOnlyList<HelpCategoryDto>>;

public sealed class ListHelpCategoriesHandler(IHelpCategoryRepository categories)
    : IQueryHandler<ListHelpCategoriesQuery, IReadOnlyList<HelpCategoryDto>>
{
    public async Task<Result<IReadOnlyList<HelpCategoryDto>>> Handle(ListHelpCategoriesQuery query, CancellationToken cancellationToken)
    {
        var locale = HelpLocaleResolver.Resolve(query.Locale);
        var list = await categories.ListAsync(locale, cancellationToken);
        var counts = await categories.CountPublishedArticlesAsync(locale, cancellationToken);

        IReadOnlyList<HelpCategoryDto> dtos = list
            .Select(c => new HelpCategoryDto(c.Id, c.Slug, c.Name, c.Locale, c.SortOrder, counts.GetValueOrDefault(c.Id)))
            .ToList();
        return Result.Success(dtos);
    }
}

/// <summary>
/// Keyword search (or browse when Q is empty) over published articles in the caller's locale; best match first
/// (title match ranked above summary/tag/body match). Plain ILIKE - tsvector/trigram search is a noted gap.
/// </summary>
[RequiresPermission("announcements.help.view")]
public sealed record SearchHelpArticlesQuery(string? Q, Guid? CategoryId, string? Locale, int? Limit) : IQuery<IReadOnlyList<HelpArticleSummaryDto>>;

public sealed class SearchHelpArticlesHandler(IHelpArticleRepository articles)
    : IQueryHandler<SearchHelpArticlesQuery, IReadOnlyList<HelpArticleSummaryDto>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
    public const int MaxTermLength = 100;

    public async Task<Result<IReadOnlyList<HelpArticleSummaryDto>>> Handle(SearchHelpArticlesQuery query, CancellationToken cancellationToken)
    {
        var locale = HelpLocaleResolver.Resolve(query.Locale);
        var term = string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim();
        if (term is { Length: > MaxTermLength })
        {
            term = term[..MaxTermLength];
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var list = await articles.SearchPublishedAsync(locale, term, query.CategoryId, limit, cancellationToken);
        IReadOnlyList<HelpArticleSummaryDto> dtos = list.Select(SupportMapper.ToSummary).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Published article by slug (slugs are globally unique across locales).</summary>
[RequiresPermission("announcements.help.view")]
public sealed record GetHelpArticleQuery(string Slug) : IQuery<HelpArticleDto>;

public sealed class GetHelpArticleHandler(IHelpArticleRepository articles) : IQueryHandler<GetHelpArticleQuery, HelpArticleDto>
{
    public async Task<Result<HelpArticleDto>> Handle(GetHelpArticleQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Slug))
        {
            return Error.NotFound(AnnouncementsDomainErrors.HelpArticleNotFound);
        }

        var article = await articles.FindPublishedBySlugAsync(query.Slug.Trim(), cancellationToken);
        return article is null ? Error.NotFound(AnnouncementsDomainErrors.HelpArticleNotFound) : SupportMapper.ToDto(article);
    }
}

/// <summary>Contact e-mail and help video channel shown on the Destek page (SupportOptions; no values in code).</summary>
[RequiresPermission("announcements.help.view")]
public sealed record GetSupportInfoQuery : IQuery<SupportInfoDto>;

public sealed class GetSupportInfoHandler(IOptions<SupportOptions> options) : IQueryHandler<GetSupportInfoQuery, SupportInfoDto>
{
    public Task<Result<SupportInfoDto>> Handle(GetSupportInfoQuery query, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var dto = new SupportInfoDto(NullIfEmpty(o.ContactEmail), NullIfEmpty(o.HelpVideoChannelUrl));
        return Task.FromResult(Result.Success(dto));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
