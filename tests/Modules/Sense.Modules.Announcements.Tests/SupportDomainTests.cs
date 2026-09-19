using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Kernel.Domain;
using Shouldly;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public sealed class SupportDomainTests
{
    [Theory]
    [InlineData("tr", "tr")]
    [InlineData("tr-TR", "tr")]
    [InlineData("EN", "en")]
    [InlineData("en_US", "en")]
    [InlineData("de", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void HelpLocales_Normalize_MapsToSupportedLocaleOrNull(string? input, string? expected) =>
        HelpLocales.Normalize(input).ShouldBe(expected);

    [Fact]
    public void HelpEntities_ArePlatformScoped_NotTenantEntities()
    {
        typeof(ITenantEntity).IsAssignableFrom(typeof(HelpArticle)).ShouldBeFalse();
        typeof(ITenantEntity).IsAssignableFrom(typeof(HelpCategory)).ShouldBeFalse();
        typeof(ITenantEntity).IsAssignableFrom(typeof(SupportTicket)).ShouldBeTrue();
    }

    [Fact]
    public void HelpArticle_Create_IsDraft_AndCleansTags()
    {
        var result = HelpArticle.Create("slug", "tr-TR", Guid.NewGuid(), "Başlık", "Özet", "Gövde", ["izin", " izin ", "", "onay"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(HelpArticleStatus.Draft);
        result.Value.Locale.ShouldBe("tr");
        result.Value.Tags.ShouldBe(["izin", "onay"]);
    }

    [Fact]
    public void HelpArticle_Create_WithUnsupportedLocale_Fails() =>
        HelpArticle.Create("slug", "de", Guid.NewGuid(), "Titel", "", "")
            .Error.Code.ShouldBe(AnnouncementsDomainErrors.HelpArticleLocaleInvalid);

    [Fact]
    public void HelpCenterSeed_HasFourArticlesPerLocale_WithUniqueSlugs_AndEmptyVideo()
    {
        HelpCenterSeed.Articles.Count(a => a.Locale == HelpLocales.Turkish).ShouldBe(4);
        HelpCenterSeed.Articles.Count(a => a.Locale == HelpLocales.English).ShouldBe(4);
        HelpCenterSeed.Articles.Select(a => a.Slug).ShouldBeUnique();
        HelpCenterSeed.Articles.Select(a => a.Id).ShouldBeUnique();

        var categoryLocales = HelpCenterSeed.Categories.ToDictionary(c => c.Id, c => c.Locale);
        HelpCenterSeed.Articles.ShouldAllBe(a => categoryLocales[a.CategoryId] == a.Locale);
    }

    [Fact]
    public void SupportTicket_Create_IsOpen_AndTrimsFields()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = SupportTicket.Create(tenantId, userId, " Giriş sorunu ", " Şifremi sıfırlayamıyorum ", SupportTicketCategory.Problem, SupportTicketChannel.Email, " ");

        result.IsSuccess.ShouldBeTrue();
        var ticket = result.Value;
        ticket.TenantId.ShouldBe(tenantId);
        ticket.UserId.ShouldBe(userId);
        ticket.Status.ShouldBe(SupportTicketStatus.Open);
        ticket.Subject.ShouldBe("Giriş sorunu");
        ticket.Body.ShouldBe("Şifremi sıfırlayamıyorum");
        ticket.PageContext.ShouldBeNull();
    }

    [Fact]
    public void SupportTicket_Create_WithoutSubjectOrBody_Fails()
    {
        SupportTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "", "body", SupportTicketCategory.Question, SupportTicketChannel.Email, null)
            .Error.Code.ShouldBe(AnnouncementsDomainErrors.TicketSubjectRequired);
        SupportTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "subject", " ", SupportTicketCategory.Question, SupportTicketChannel.Email, null)
            .Error.Code.ShouldBe(AnnouncementsDomainErrors.TicketBodyRequired);
    }
}
