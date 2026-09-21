using NSubstitute;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Application.ReadRecords;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class AnnouncementReadRecordQueriesTests
{
    [Fact]
    public async Task ListAnnouncementReadRecordsQuery_WithValidQuery_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.GetByAnnouncementIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>())
            .Returns(new List<AnnouncementReadRecord>());

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new ListAnnouncementReadRecordsHandler(repository, tenantContext);
        var result = await handler.Handle(new ListAnnouncementReadRecordsQuery(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task CountAnnouncementReadersQuery_WithValidQuery_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.CountByAnnouncementIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>()).Returns(5);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new CountAnnouncementReadersHandler(repository, tenantContext);
        var result = await handler.Handle(new CountAnnouncementReadersQuery(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public async Task MarkAnnouncementRead_WhenNotYetRead_AddsRecord()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.ExistsAsync(tenantId, announcementId, employeeId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new MarkAnnouncementReadHandler(tenant, currentUser, PublishedAnnouncementRepository(tenantId, announcementId), repository);
        var result = await handler.Handle(new MarkAnnouncementReadCommand(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Received(1).Add(Arg.Is<AnnouncementReadRecord>(r => r.AnnouncementId == announcementId && r.EmployeeId == employeeId));
    }

    [Fact]
    public async Task MarkAnnouncementRead_WhenAlreadyRead_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var repository = Substitute.For<IAnnouncementReadRecordRepository>();
        repository.ExistsAsync(tenantId, announcementId, employeeId, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new MarkAnnouncementReadHandler(tenant, currentUser, PublishedAnnouncementRepository(tenantId, announcementId), repository);
        var result = await handler.Handle(new MarkAnnouncementReadCommand(announcementId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.DidNotReceive().Add(Arg.Any<AnnouncementReadRecord>());
    }

    [Fact]
    public async Task MarkAnnouncementRead_WhenAnnouncementIsDraft_ReturnsNotFoundAndAddsNothing()
    {
        var tenantId = Guid.NewGuid();
        var draft = Announcement.Create(tenantId, "Taslak", "İçerik", AnnouncementType.General, AnnouncementPriority.Normal).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(Guid.NewGuid());
        var announcements = Substitute.For<IAnnouncementRepository>();
        announcements.GetByIdAsync(tenantId, draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        var repository = Substitute.For<IAnnouncementReadRecordRepository>();

        var handler = new MarkAnnouncementReadHandler(tenant, currentUser, announcements, repository);
        var result = await handler.Handle(new MarkAnnouncementReadCommand(draft.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        repository.DidNotReceive().Add(Arg.Any<AnnouncementReadRecord>());
    }

    [Fact]
    public async Task ListMyAnnouncementFeed_FlagsAnnouncementsTheEmployeeHasRead()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var read = Published(tenantId, "Okunan");
        var unread = Published(tenantId, "Yeni");

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var announcements = Substitute.For<IAnnouncementRepository>();
        announcements.GetPublishedAsync(tenantId, Arg.Any<CancellationToken>()).Returns([unread, read]);
        var readRecords = Substitute.For<IAnnouncementReadRecordRepository>();
        readRecords.GetReadAnnouncementIdsAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns([read.Id]);

        var handler = new ListMyAnnouncementFeedHandler(announcements, readRecords, tenant, currentUser);
        var result = await handler.Handle(new ListMyAnnouncementFeedQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([unread.Id, read.Id], result.Value.Select(i => i.Id));
        Assert.False(result.Value[0].IsRead);
        Assert.True(result.Value[1].IsRead);
    }

    [Fact]
    public async Task ListMyAnnouncementFeed_WithoutEmployeeRecord_ShowsAllUnread()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);
        var announcements = Substitute.For<IAnnouncementRepository>();
        announcements.GetPublishedAsync(tenantId, Arg.Any<CancellationToken>()).Returns([Published(tenantId, "Duyuru")]);
        var readRecords = Substitute.For<IAnnouncementReadRecordRepository>();

        var handler = new ListMyAnnouncementFeedHandler(announcements, readRecords, tenant, currentUser);
        var result = await handler.Handle(new ListMyAnnouncementFeedQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(Assert.Single(result.Value).IsRead);
        await readRecords.DidNotReceiveWithAnyArgs().GetReadAnnouncementIdsAsync(default, default, default);
    }

    private static Announcement Published(Guid tenantId, string title)
    {
        var announcement = Announcement.Create(tenantId, title, "İçerik", AnnouncementType.General, AnnouncementPriority.Normal).Value;
        announcement.Publish(Guid.NewGuid());
        return announcement;
    }

    private static IAnnouncementRepository PublishedAnnouncementRepository(Guid tenantId, Guid announcementId)
    {
        var announcement = Published(tenantId, "Duyuru");
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>()).Returns(announcement);
        return repository;
    }
}
