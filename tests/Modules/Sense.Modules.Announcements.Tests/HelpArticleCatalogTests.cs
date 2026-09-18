using Sense.Modules.Announcements.Application.Support;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class HelpArticleCatalogTests
{
    [Fact]
    public void Search_WithTurkishKeyword_FindsMatchingArticle()
    {
        var results = HelpArticleCatalog.Search("veri aktarımı");
        Assert.Contains(results, a => a.Slug == "veri-aktarimi-izin-gunleri");
    }

    [Fact]
    public void Search_IsCaseInsensitiveForTurkishDottedI()
    {
        // "İZİN" (dotted capital İ) küçük harfe Türkçe kültürde "izin" olur; invariant/ordinal ToLower ise "i̇zin"
        // üretir (Türkçe İ/ı sorunu) — CultureInfo.GetCultureInfo("tr-TR") kullanılmazsa bu arama eşleşmezdi.
        var results = HelpArticleCatalog.Search("İZİN");
        Assert.Contains(results, a => a.Slug == "izin-devretme-secenekleri");
        Assert.Contains(results, a => a.Slug == "yonetici-adina-izin-ekleme");
    }

    [Fact]
    public void Search_WithEmptyTerm_ReturnsAllArticles()
    {
        var results = HelpArticleCatalog.Search("   ");
        Assert.Equal(HelpArticleCatalog.Articles.Count, results.Count);
    }

    [Fact]
    public void ForRoute_ReturnsOnlyArticlesRelatedToThatRoute()
    {
        var results = HelpArticleCatalog.ForRoute("/admin/settings/import");
        Assert.All(results, a => Assert.Contains("/admin/settings/import", a.RelatedRoutes));
    }

    [Fact]
    public void GetBySlug_UnknownSlug_ReturnsNull()
    {
        Assert.Null(HelpArticleCatalog.GetBySlug("bilinmeyen-makale"));
    }

    [Fact]
    public async Task HelpArticleReader_ReturnsAllArticlesForAiConsumption()
    {
        var reader = new HelpArticleReader();
        var result = await reader.GetAllAsync();
        Assert.Equal(HelpArticleCatalog.Articles.Count, result.Count);
    }
}
