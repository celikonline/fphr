using NSubstitute;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class AnnouncementQueriesTests
{
    [Fact]
    public async Task GetAnnouncementQuery_WithValidId_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var announcement = Announcement.Create(tenantId, "Test Announcement", "This is test content", AnnouncementType.General, AnnouncementPriority.Normal).Value;

        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcement.Id, Arg.Any<CancellationToken>()).Returns(announcement);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new GetAnnouncementHandler(repository, tenantContext);
        var result = await handler.Handle(new GetAnnouncementQuery(announcement.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(announcement.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetAnnouncementQuery_WithInvalidId_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Announcement?)null);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new GetAnnouncementHandler(repository, tenantContext);
        var result = await handler.Handle(new GetAnnouncementQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ListAnnouncementsQuery_PublishedOnly_ReturnsOnlyPublished()
    {
        var tenantId = Guid.NewGuid();
        var published = Announcement.Create(tenantId, "Published", "Content", AnnouncementType.General, AnnouncementPriority.Normal).Value;

        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetPublishedAsync(tenantId, Arg.Any<CancellationToken>()).Returns([published]);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new ListAnnouncementsHandler(repository, tenantContext);
        var result = await handler.Handle(new ListAnnouncementsQuery(PublishedOnly: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
    }
}
