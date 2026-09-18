using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.Support;

public sealed record HelpArticleSummaryDto(string Slug, string Category, string Title, IReadOnlyList<string> RelatedRoutes);

public sealed record HelpArticleDetailDto(string Slug, string Category, string Title, string Body, IReadOnlyList<string> Media);

/// <summary>
/// Yardım Merkezi arama — permissionsız (izin gerektirmez, her kimliği doğrulanmış kullanıcı okuyabilir; bkz.
/// AuthorizationBehaviour: [RequiresPermission] yoksa yalnız kimlik doğrulaması aranır). Route verilirse önce
/// sayfa bağlamlı makaleler (11.3.13: "sayfa bağlamlı yardım"), sonra anahtar kelimeyle eşleşenler döner.
/// Sonuç sayısı content-as-code geri besleme döngüsü için HelpSearchLog'a yazılır.
/// </summary>
public sealed record SearchHelpArticlesQuery(string? Term = null, string? Route = null) : IQuery<IReadOnlyList<HelpArticleSummaryDto>>;

public sealed class SearchHelpArticlesHandler(ITenantContext tenant, IHelpSearchLogRepository searchLog)
    : IQueryHandler<SearchHelpArticlesQuery, IReadOnlyList<HelpArticleSummaryDto>>
{
    public Task<Result<IReadOnlyList<HelpArticleSummaryDto>>> Handle(SearchHelpArticlesQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<HelpArticle> byRoute = string.IsNullOrWhiteSpace(query.Route)
            ? []
            : HelpArticleCatalog.ForRoute(query.Route);

        IReadOnlyList<HelpArticle> matches = string.IsNullOrWhiteSpace(query.Term)
            ? byRoute
            : byRoute.Concat(HelpArticleCatalog.Search(query.Term)).DistinctBy(a => a.Slug).ToList();

        if (matches.Count == 0 && string.IsNullOrWhiteSpace(query.Term) && string.IsNullOrWhiteSpace(query.Route))
        {
            matches = HelpArticleCatalog.Articles;
        }

        if (!string.IsNullOrWhiteSpace(query.Term) && tenant.IsResolved)
        {
            searchLog.Add(HelpSearchLog.Create(tenant.TenantId, query.Term, matches.Count, query.Route));
        }

        IReadOnlyList<HelpArticleSummaryDto> dtos = matches
            .Select(a => new HelpArticleSummaryDto(a.Slug, a.Category, a.LocalizedTitle(), a.RelatedRoutes))
            .ToList();

        return Task.FromResult(Result.Success(dtos));
    }
}

/// <summary>Tek bir makaleyi slug ile getir. İzin gerektirmez.</summary>
public sealed record GetHelpArticleQuery(string Slug) : IQuery<HelpArticleDetailDto>;

public sealed class GetHelpArticleHandler : IQueryHandler<GetHelpArticleQuery, HelpArticleDetailDto>
{
    public Task<Result<HelpArticleDetailDto>> Handle(GetHelpArticleQuery query, CancellationToken cancellationToken)
    {
        var article = HelpArticleCatalog.GetBySlug(query.Slug);
        if (article is null)
        {
            return Task.FromResult(Result.Failure<HelpArticleDetailDto>(Error.NotFound(HelpArticleErrors.NotFound)));
        }

        return Task.FromResult(Result.Success(new HelpArticleDetailDto(article.Slug, article.Category, article.LocalizedTitle(), article.LocalizedBody(), article.Media)));
    }
}

internal static class HelpArticleErrors
{
    public const string NotFound = "help_article.not_found";
}
