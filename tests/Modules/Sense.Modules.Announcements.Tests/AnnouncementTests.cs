using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Announcements;
using Shouldly;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public sealed class AnnouncementTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private static AnnouncementContent General(string title = "Yeni ofis kuralları") => new(title, "Detaylar ekte.", AnnouncementCategory.General);

    private static AnnouncementContent Event(DateTime? startsAt, DateTime? endsAt = null, string? meetingUrl = null) =>
        new("Şirket kahvaltısı", "Hepinizi bekleriz.", AnnouncementCategory.Event, startsAt, endsAt, "Kat 3", meetingUrl);

    private static Announcement Draft(AnnouncementContent? content = null) => Announcement.CreateDraft(TenantId, content ?? General()).Value;

    [Fact]
    public void CreateDraft_WithValidGeneralContent_IsDraftAndUnpinned()
    {
        var result = Announcement.CreateDraft(TenantId, General("  Başlık  "));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(AnnouncementStatus.Draft);
        result.Value.IsPinned.ShouldBeFalse();
        result.Value.Title.ShouldBe("Başlık");
        result.Value.PublishedAt.ShouldBeNull();
    }

    [Fact]
    public void CreateDraft_WithoutTitle_FailsValidation()
    {
        var result = Announcement.CreateDraft(TenantId, General(" "));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AnnouncementsDomainErrors.TitleRequired);
    }

    [Fact]
    public void CreateDraft_EventWithoutStart_Fails()
    {
        var result = Announcement.CreateDraft(TenantId, Event(startsAt: null));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AnnouncementsDomainErrors.EventStartRequired);
    }

    [Fact]
    public void CreateDraft_EventEndingBeforeStart_Fails()
    {
        var result = Announcement.CreateDraft(TenantId, Event(Now.AddDays(2), Now.AddDays(1)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AnnouncementsDomainErrors.EventEndBeforeStart);
    }

    [Fact]
    public void CreateDraft_EventEndingAtStart_IsAllowed()
    {
        var start = Now.AddDays(2);

        Announcement.CreateDraft(TenantId, Event(start, start)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CreateDraft_EventWithNonHttpMeetingUrl_Fails()
    {
        var result = Announcement.CreateDraft(TenantId, Event(Now.AddDays(1), meetingUrl: "javascript:alert(1)"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AnnouncementsDomainErrors.MeetingUrlInvalid);
    }

    [Fact]
    public void CreateDraft_EventWithAllFields_KeepsThemInUtc()
    {
        var start = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Unspecified);
        var result = Announcement.CreateDraft(TenantId, Event(start, start.AddHours(2), "https://meet.example.com/abc"));

        result.IsSuccess.ShouldBeTrue();
        var a = result.Value;
        a.EventStartsAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        a.EventEndsAt.ShouldBe(a.EventStartsAt.Value.AddHours(2));
        a.Location.ShouldBe("Kat 3");
        a.MeetingUrl.ShouldBe("https://meet.example.com/abc");
    }

    [Theory]
    [InlineData(AnnouncementCategory.General)]
    [InlineData(AnnouncementCategory.PolicyUpdate)]
    public void CreateDraft_NonEventWithEventFields_Fails(AnnouncementCategory category)
    {
        var withStart = new AnnouncementContent("Başlık", "Metin", category, EventStartsAt: Now);
        var withLocation = new AnnouncementContent("Başlık", "Metin", category, Location: "Kat 3");

        Announcement.CreateDraft(TenantId, withStart).Error.Code.ShouldBe(AnnouncementsDomainErrors.EventFieldsNotAllowed);
        Announcement.CreateDraft(TenantId, withLocation).Error.Code.ShouldBe(AnnouncementsDomainErrors.EventFieldsNotAllowed);
    }

    [Fact]
    public void Update_SwitchingEventToGeneral_ClearsEventFields()
    {
        var announcement = Draft(Event(Now.AddDays(3), Now.AddDays(3).AddHours(1)));

        announcement.Update(General()).IsSuccess.ShouldBeTrue();

        announcement.Category.ShouldBe(AnnouncementCategory.General);
        announcement.EventStartsAt.ShouldBeNull();
        announcement.EventEndsAt.ShouldBeNull();
        announcement.Location.ShouldBeNull();
    }

    [Fact]
    public void Publish_FromDraft_SetsPublishedFields()
    {
        var announcement = Draft();
        var userId = Guid.NewGuid();

        announcement.Publish(userId, Now).IsSuccess.ShouldBeTrue();

        announcement.Status.ShouldBe(AnnouncementStatus.Published);
        announcement.PublishedAt.ShouldBe(Now);
        announcement.PublishedByUserId.ShouldBe(userId);
    }

    [Fact]
    public void Publish_Twice_Fails()
    {
        var announcement = Draft();
        announcement.Publish(null, Now);

        announcement.Publish(null, Now.AddMinutes(1)).Error.Code.ShouldBe(AnnouncementsDomainErrors.NotDraft);
        announcement.PublishedAt.ShouldBe(Now);
    }

    [Fact]
    public void Archive_FromPublishedPinned_UnpinsAndBlocksFurtherChanges()
    {
        var announcement = Draft();
        announcement.Publish(null, Now);
        announcement.SetPinned(true).IsSuccess.ShouldBeTrue();

        announcement.Archive().IsSuccess.ShouldBeTrue();

        announcement.Status.ShouldBe(AnnouncementStatus.Archived);
        announcement.IsPinned.ShouldBeFalse();
        announcement.Archive().Error.Code.ShouldBe(AnnouncementsDomainErrors.AlreadyArchived);
        announcement.Update(General("Değişti")).Error.Code.ShouldBe(AnnouncementsDomainErrors.AlreadyArchived);
        announcement.Publish(null, Now).Error.Code.ShouldBe(AnnouncementsDomainErrors.NotDraft);
    }

    [Fact]
    public void Archive_FromDraft_IsAllowed()
    {
        var announcement = Draft();

        announcement.Archive().IsSuccess.ShouldBeTrue();
        announcement.Status.ShouldBe(AnnouncementStatus.Archived);
    }

    [Fact]
    public void Pin_RequiresPublished_UnpinAlwaysAllowed()
    {
        var announcement = Draft();

        announcement.SetPinned(true).Error.Code.ShouldBe(AnnouncementsDomainErrors.NotPublished);
        announcement.SetPinned(false).IsSuccess.ShouldBeTrue();

        announcement.Publish(null, Now);
        announcement.SetPinned(true).IsSuccess.ShouldBeTrue();
        announcement.IsPinned.ShouldBeTrue();
        announcement.SetPinned(false).IsSuccess.ShouldBeTrue();
        announcement.IsPinned.ShouldBeFalse();
    }
}
