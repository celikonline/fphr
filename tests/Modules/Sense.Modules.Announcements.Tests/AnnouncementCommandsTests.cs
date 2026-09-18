using NSubstitute;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
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

        var handler = new PublishAnnouncementHandler(tenant, currentUser, repository);
        var result = await handler.Handle(new PublishAnnouncementCommand(announcement.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AnnouncementStatus.Published, announcement.Status);
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
}
