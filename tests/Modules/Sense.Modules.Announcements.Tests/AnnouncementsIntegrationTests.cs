using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>
/// Integration tests (real PostgreSQL via Testcontainers, see TestFixture) for the Announcements repositories: feed
/// ordering, the upcoming-events window, Help Center search ranking/locale/published-only filtering, platform scope
/// of help content (same rows for every tenant, seeded by the InitialAnnouncements migration) and tenant isolation of
/// support tickets.
/// </summary>
[Collection("PostgreSQL")]
public sealed class AnnouncementsIntegrationTests : TestFixture
{
    private static readonly DateTime Now = DateTime.UtcNow;

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<AnnouncementsDbContext>(configuration, AnnouncementsDbContext.SchemaName);
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IHelpCategoryRepository, HelpCategoryRepository>();
        services.AddScoped<IHelpArticleRepository, HelpArticleRepository>();
        services.AddScoped<ISupportTicketRepository, SupportTicketRepository>();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AnnouncementsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Feed_ListsPublishedOnly_PinnedFirst_ThenNewest()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AnnouncementsDbContext>();
            var repo = write.ServiceProvider.GetRequiredService<IAnnouncementRepository>();

            repo.Add(Published(tenantId, "Eski", Now.AddDays(-3)));
            repo.Add(Published(tenantId, "Yeni", Now.AddDays(-1)));
            repo.Add(Published(tenantId, "Sabit eski", Now.AddDays(-10), pinned: true));
            repo.Add(Announcement.CreateDraft(tenantId, Content("Taslak")).Value);
            var archived = Published(tenantId, "Arşiv", Now);
            archived.Archive();
            repo.Add(archived);
            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider!.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAnnouncementRepository>();

        var feed = await reader.ListFeedAsync(null, 0, 10);
        feed.Select(a => a.Title).ShouldBe(["Sabit eski", "Yeni", "Eski"]);
        (await reader.CountFeedAsync(null)).ShouldBe(3);
        (await reader.CountAllAsync(null)).ShouldBe(5);
        (await reader.ListFeedAsync(null, 1, 1)).Single().Title.ShouldBe("Yeni");
    }

    [Fact]
    public async Task UpcomingEvents_ReturnsPublishedEventsInWindow_IncludingOngoing_SoonestFirst()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AnnouncementsDbContext>();
            var repo = write.ServiceProvider.GetRequiredService<IAnnouncementRepository>();

            repo.Add(PublishedEvent(tenantId, "10 gün sonra", Now.AddDays(10), null));
            repo.Add(PublishedEvent(tenantId, "2 gün sonra", Now.AddDays(2), Now.AddDays(2).AddHours(2)));
            repo.Add(PublishedEvent(tenantId, "Devam ediyor", Now.AddHours(-1), Now.AddHours(1)));
            repo.Add(PublishedEvent(tenantId, "Pencere dışı", Now.AddDays(40), null));
            repo.Add(PublishedEvent(tenantId, "Bitti", Now.AddDays(-2), Now.AddDays(-2).AddHours(3)));
            repo.Add(Announcement.CreateDraft(tenantId, EventContent("Taslak etkinlik", Now.AddDays(3), null)).Value);
            repo.Add(Published(tenantId, "Genel duyuru", Now));
            await db.SaveChangesAsync();
        }

        using (var otherTenant = TenantContextSetter!.BeginScope(Guid.NewGuid()))
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AnnouncementsDbContext>();
            var repo = write.ServiceProvider.GetRequiredService<IAnnouncementRepository>();
            repo.Add(PublishedEvent(Guid.Empty, "Başka şirket", Now.AddDays(1), null));
            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider!.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAnnouncementRepository>();

        var events = await reader.ListUpcomingEventsAsync(Now, Now.AddDays(AnnouncementsLimits.UpcomingEventsDefaultDays), 10);
        events.Select(a => a.Title).ShouldBe(["Devam ediyor", "2 gün sonra", "10 gün sonra"]);

        (await reader.ListUpcomingEventsAsync(Now, Now.AddDays(AnnouncementsLimits.UpcomingEventsDefaultDays), 1)).Single().Title.ShouldBe("Devam ediyor");
    }

    [Fact]
    public async Task HelpCenter_SeededArticles_AreVisibleToEveryTenant_AndFilteredByLocale()
    {
        foreach (var tenantId in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
            using var scope = ServiceProvider!.CreateScope();
            var articles = scope.ServiceProvider.GetRequiredService<IHelpArticleRepository>();
            var categories = scope.ServiceProvider.GetRequiredService<IHelpCategoryRepository>();

            var tr = await articles.SearchPublishedAsync(HelpLocales.Turkish, null, null, 50);
            var en = await articles.SearchPublishedAsync(HelpLocales.English, null, null, 50);

            tr.Count.ShouldBe(4);
            tr.ShouldAllBe(a => a.Locale == HelpLocales.Turkish);
            en.Count.ShouldBe(4);
            en.ShouldAllBe(a => a.Locale == HelpLocales.English);
            (await categories.ListAsync(HelpLocales.Turkish)).Count.ShouldBe(4);
            (await categories.CountPublishedArticlesAsync(HelpLocales.English)).Values.Sum().ShouldBe(4);

            var first = (await articles.SearchPublishedAsync(HelpLocales.Turkish, "veri aktarımı", null, 5)).First();
            first.Slug.ShouldBe("veri-aktarimi-izin-bilgileri");
            (await articles.FindPublishedBySlugAsync("access-settings"))!.Locale.ShouldBe(HelpLocales.English);
        }

        // Written without a tenant filter or tenant stamp: no tenant_id column exists on the platform tables.
        using var check = ServiceProvider!.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AnnouncementsDbContext>();
        db.Model.FindEntityType(typeof(HelpArticle))!.FindProperty("TenantId").ShouldBeNull();
        db.Model.FindEntityType(typeof(HelpArticle))!.GetDeclaredQueryFilters().ShouldBeEmpty();
    }

    [Fact]
    public async Task HelpSearch_RanksTitleAboveBody_AndSkipsDraftAndOtherLocale()
    {
        var categoryId = HelpCenterSeed.Categories.First(c => c.Locale == HelpLocales.Turkish).Id;
        var enCategoryId = HelpCenterSeed.Categories.First(c => c.Locale == HelpLocales.English).Id;

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AnnouncementsDbContext>();
            var articles = write.ServiceProvider.GetRequiredService<IHelpArticleRepository>();

            articles.Add(PublishedArticle("a-body-match", HelpLocales.Turkish, categoryId, "Alfa makale", "Özet", "Bu gövde Zebrafon kelimesini içerir."));
            articles.Add(PublishedArticle("b-tag-match", HelpLocales.Turkish, categoryId, "Beta makale", "Özet", "Gövde", tags: ["zebrafon"]));
            articles.Add(PublishedArticle("c-title-match", HelpLocales.Turkish, categoryId, "Zebrafon kurulumu", "Özet", "Gövde"));
            articles.Add(PublishedArticle("d-summary-match", HelpLocales.Turkish, categoryId, "Delta makale", "ZEBRAFON özeti", "Gövde"));
            articles.Add(HelpArticle.Create("e-draft", HelpLocales.Turkish, categoryId, "Zebrafon taslak", "Özet", "Gövde").Value);
            articles.Add(PublishedArticle("f-english", HelpLocales.English, enCategoryId, "Zebrafon setup", "Summary", "Body"));
            var archived = PublishedArticle("g-archived", HelpLocales.Turkish, categoryId, "Zebrafon eski", "Özet", "Gövde");
            archived.Archive();
            articles.Add(archived);
            await db.SaveChangesAsync();
        }

        using var read = ServiceProvider!.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IHelpArticleRepository>();

        var results = await reader.SearchPublishedAsync(HelpLocales.Turkish, "zebrafon", null, 20);
        results.Select(a => a.Slug).ShouldBe(["c-title-match", "d-summary-match", "b-tag-match", "a-body-match"]);

        (await reader.SearchPublishedAsync(HelpLocales.English, "zebrafon", null, 20)).Select(a => a.Slug).ShouldBe(["f-english"]);
        (await reader.FindPublishedBySlugAsync("e-draft")).ShouldBeNull();
        (await reader.SearchPublishedAsync(HelpLocales.Turkish, "100%_", null, 20)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SupportTickets_AreTenantIsolated_AndListedPerUser()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        using (TenantContextSetter!.BeginScope(tenantA))
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AnnouncementsDbContext>();
            var tickets = write.ServiceProvider.GetRequiredService<ISupportTicketRepository>();
            tickets.Add(Ticket(tenantA, userId, "Birinci"));
            tickets.Add(Ticket(tenantA, userId, "İkinci"));
            tickets.Add(Ticket(tenantA, otherUserId, "Başkasının"));
            await db.SaveChangesAsync();
        }

        using (TenantContextSetter!.BeginScope(tenantA))
        using (var read = ServiceProvider!.CreateScope())
        {
            var tickets = read.ServiceProvider.GetRequiredService<ISupportTicketRepository>();
            (await tickets.CountByUserAsync(userId)).ShouldBe(2);
            (await tickets.ListByUserAsync(userId, 0, 10)).Select(t => t.Subject).ShouldBe(["İkinci", "Birinci"], ignoreOrder: true);
        }

        using (TenantContextSetter!.BeginScope(tenantB))
        using (var read = ServiceProvider!.CreateScope())
        {
            var tickets = read.ServiceProvider.GetRequiredService<ISupportTicketRepository>();
            (await tickets.CountByUserAsync(userId)).ShouldBe(0);
            (await tickets.ListByUserAsync(userId, 0, 10)).ShouldBeEmpty();
        }
    }

    private static AnnouncementContent Content(string title) => new(title, "Metin", AnnouncementCategory.General);

    private static AnnouncementContent EventContent(string title, DateTime startsAt, DateTime? endsAt) =>
        new(title, "Metin", AnnouncementCategory.Event, startsAt, endsAt, "Toplantı salonu", null);

    private static Announcement Published(Guid tenantId, string title, DateTime publishedAt, bool pinned = false)
    {
        var a = Announcement.CreateDraft(tenantId, Content(title)).Value;
        a.Publish(null, publishedAt);
        if (pinned)
        {
            a.SetPinned(true);
        }

        return a;
    }

    private static Announcement PublishedEvent(Guid tenantId, string title, DateTime startsAt, DateTime? endsAt)
    {
        var a = Announcement.CreateDraft(tenantId, EventContent(title, startsAt, endsAt)).Value;
        a.Publish(null, Now.AddDays(-5));
        return a;
    }

    private static HelpArticle PublishedArticle(
        string slug, string locale, Guid categoryId, string title, string summary, string body, IEnumerable<string>? tags = null)
    {
        var article = HelpArticle.Create(slug, locale, categoryId, title, summary, body, tags).Value;
        article.Publish(Now);
        return article;
    }

    private static SupportTicket Ticket(Guid tenantId, Guid userId, string subject) =>
        SupportTicket.Create(tenantId, userId, subject, "Metin", SupportTicketCategory.Question, SupportTicketChannel.Email, null).Value;
}
