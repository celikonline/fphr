using NSubstitute;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class AnnouncementCommandsTests
{
    [Fact]
    public async Task CreateAnnouncement_WithValidCommand_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var command = new CreateAnnouncementCommand("Test Announcement", "This is a test announcement", AnnouncementType.General, AnnouncementPriority.High);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var repository = Substitute.For<IAnnouncementRepository>();
        var handler = new CreateAnnouncementHandler(tenant, repository);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        repository.Received(1).Add(Arg.Is<Announcement>(a => a.TenantId == tenantId && a.Title == "Test Announcement"));
    }

    /// <summary>TK-016: ScheduledFor verilirse duyuru taslak yerine Scheduled olarak oluşturulur (elle Publish gerekmez).</summary>
    [Fact]
    public async Task CreateAnnouncement_WithScheduledFor_ShouldBeScheduled_NotDraft()
    {
        var tenantId = Guid.NewGuid();
        var scheduledFor = DateTime.UtcNow.AddDays(1);
        var command = new CreateAnnouncementCommand("Test Announcement", "This is a test announcement", AnnouncementType.General, AnnouncementPriority.High, ScheduledFor: scheduledFor);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var repository = Substitute.For<IAnnouncementRepository>();
        Announcement? created = null;
        repository.When(r => r.Add(Arg.Any<Announcement>())).Do(call => created = call.Arg<Announcement>());

        var handler = new CreateAnnouncementHandler(tenant, repository);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(created);
        Assert.Equal(AnnouncementStatus.Scheduled, created!.Status);
        Assert.Equal(scheduledFor, created.ScheduledFor);
    }

    /// <summary>TK-016 kabul kriteri #3: ScheduledFor verilmezse davranış değişmedi (taslak, elle Publish gerekir).</summary>
    [Fact]
    public async Task CreateAnnouncement_WithoutScheduledFor_ShouldStayDraft()
    {
        var tenantId = Guid.NewGuid();
        var command = new CreateAnnouncementCommand("Test Announcement", "This is a test announcement", AnnouncementType.General, AnnouncementPriority.High);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var repository = Substitute.For<IAnnouncementRepository>();
        Announcement? created = null;
        repository.When(r => r.Add(Arg.Any<Announcement>())).Do(call => created = call.Arg<Announcement>());

        var handler = new CreateAnnouncementHandler(tenant, repository);
        await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(AnnouncementStatus.Draft, created!.Status);
        Assert.Null(created.ScheduledFor);
    }

    /// <summary>TK-016: PublishScheduledAnnouncementsJob'ın çağırdığı domain metodu - zamanı gelmiş bir Scheduled duyuruyu yayınlar.</summary>
    [Fact]
    public void PublishScheduled_WhenScheduled_ShouldPublish()
    {
        var tenantId = Guid.NewGuid();
        var scheduledFor = new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc);
        var announcement = Announcement.Create(
            tenantId, "Title", "Content", AnnouncementType.General, AnnouncementPriority.Normal, scheduledFor: scheduledFor).Value;
        Assert.Equal(AnnouncementStatus.Scheduled, announcement.Status);

        var now = scheduledFor.AddMinutes(1);
        var result = announcement.PublishScheduled(now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnouncementStatus.Published, announcement.Status);
        Assert.Equal(now, announcement.PublishedAt);
    }

    /// <summary>Zaten yayınlanmış/taslak bir duyuru için PublishScheduled çağrısı bir hata döner (yalnız Scheduled durumdan geçer).</summary>
    [Fact]
    public void PublishScheduled_WhenNotScheduled_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var announcement = Announcement.Create(tenantId, "Title", "Content", AnnouncementType.General, AnnouncementPriority.Normal).Value;

        var result = announcement.PublishScheduled(DateTime.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementDomainErrors.CannotPublish, result.Error.Code);
        Assert.Equal(AnnouncementStatus.Draft, announcement.Status);
    }

    [Fact]
    public void CreateAnnouncementValidator_WithEmptyTitle_ShouldFail()
    {
        var command = new CreateAnnouncementCommand("", "Some content", AnnouncementType.General, AnnouncementPriority.Normal);
        var result = new CreateAnnouncementValidator().Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateAnnouncementValidator_WithEmptyContent_ShouldFail()
    {
        var command = new CreateAnnouncementCommand("Valid Title", "", AnnouncementType.General, AnnouncementPriority.Normal);
        var result = new CreateAnnouncementValidator().Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task PublishAnnouncement_WhenDraft_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var announcement = Announcement.Create(tenantId, "Title", "Content", AnnouncementType.General, AnnouncementPriority.Normal).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcement.Id, Arg.Any<CancellationToken>()).Returns(announcement);

        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var handler = new PublishAnnouncementHandler(tenant, currentUser, repository, outbox);
        var result = await handler.Handle(new PublishAnnouncementCommand(announcement.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnouncementStatus.Published, announcement.Status);
        outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task PublishAnnouncement_WithNotifyEmployees_EnqueuesAnnouncementPublished()
    {
        var tenantId = Guid.NewGuid();
        var announcement = Announcement.Create(
            tenantId, "Yemekhane", "<p>Yarın <strong>kapalı</strong></p><p>Detaylar &amp; saat</p>", AnnouncementType.General, AnnouncementPriority.Urgent).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcement.Id, Arg.Any<CancellationToken>()).Returns(announcement);
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var handler = new PublishAnnouncementHandler(tenant, currentUser, repository, outbox);
        var result = await handler.Handle(new PublishAnnouncementCommand(announcement.Id, NotifyEmployees: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        outbox.Received(1).Enqueue(Arg.Is<AnnouncementPublished>(e =>
            e.TenantId == tenantId && e.AnnouncementId == announcement.Id && e.Title == "Yemekhane"
            && e.Excerpt == "Yarın kapalı Detaylar & saat" && e.IsHighPriority));
    }

    [Fact]
    public async Task ArchiveAnnouncement_WhenNotFound_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Announcement?)null);

        var handler = new ArchiveAnnouncementHandler(tenant, repository);
        var result = await handler.Handle(new ArchiveAnnouncementCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementDomainErrors.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task UpdateAnnouncement_WhenDraft_ShouldChangeTitleAndContent()
    {
        var tenantId = Guid.NewGuid();
        var announcement = Announcement.Create(tenantId, "Old title", "Old content", AnnouncementType.General, AnnouncementPriority.Normal).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcement.Id, Arg.Any<CancellationToken>()).Returns(announcement);

        var handler = new UpdateAnnouncementHandler(tenant, repository);
        var result = await handler.Handle(
            new UpdateAnnouncementCommand(announcement.Id, "New title", "<p>New <strong>rich</strong> content</p>", AnnouncementType.HR, AnnouncementPriority.High), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New title", announcement.Title);
        Assert.Equal("<p>New <strong>rich</strong> content</p>", announcement.Content);
        Assert.Equal(AnnouncementType.HR, announcement.Type);
    }

    [Fact]
    public async Task UpdateAnnouncement_WhenAlreadyPublished_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var announcement = Announcement.Create(tenantId, "Title", "Content", AnnouncementType.General, AnnouncementPriority.Normal).Value;
        announcement.Publish(Guid.NewGuid());

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcement.Id, Arg.Any<CancellationToken>()).Returns(announcement);

        var handler = new UpdateAnnouncementHandler(tenant, repository);
        var result = await handler.Handle(
            new UpdateAnnouncementCommand(announcement.Id, "New title", "New content", AnnouncementType.General, AnnouncementPriority.Normal), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementDomainErrors.CannotEditPublished, result.Error.Code);
    }

    [Fact]
    public async Task CreateAnnouncement_StripsScriptTagsFromContent()
    {
        var tenantId = Guid.NewGuid();
        var command = new CreateAnnouncementCommand(
            "Title", "<p>Hello</p><script>alert('xss')</script>", AnnouncementType.General, AnnouncementPriority.Normal);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<IAnnouncementRepository>();
        Announcement? created = null;
        repository.When(r => r.Add(Arg.Any<Announcement>())).Do(call => created = call.Arg<Announcement>());

        var handler = new CreateAnnouncementHandler(tenant, repository);
        await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(created);
        Assert.DoesNotContain("<script", created!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Hello</p>", created.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void AnnouncementContentSanitizer_RemovesEventHandlerAttributes()
    {
        var sanitized = AnnouncementContentSanitizer.Sanitize("<img src=\"x.png\" onerror=\"alert(1)\" />");

        Assert.DoesNotContain("onerror", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnnouncementContentSanitizer_KeepsDataAttachmentIdForInlineImages()
    {
        var id = Guid.NewGuid();
        var sanitized = AnnouncementContentSanitizer.Sanitize($"<img data-attachment-id=\"{id}\" alt=\"logo\" />");

        Assert.Contains($"data-attachment-id=\"{id}\"", sanitized, StringComparison.Ordinal);
    }
}
