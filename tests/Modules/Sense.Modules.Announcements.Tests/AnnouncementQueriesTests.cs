using NSubstitute;
using Sense.Shared.Contracts.Tenants;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Domain;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

[Trait("Category", "Integration")]
public class AnnouncementQueriesTests
{
    [Fact]
    public async Task GetAnnouncementQuery_WithValidId_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();
        var query = new GetAnnouncementQuery(announcementId, tenantId);

        var announcement = Announcement.Create(
            tenantId,
            "Test Announcement",
            "This is test content",
            AnnouncementType.General,
            AnnouncementPriority.Normal
        ).Value;

        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>())
            .Returns(announcement);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new GetAnnouncementHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(announcement.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetAnnouncementQuery_WithInvalidId_ShouldFail()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();
        var query = new GetAnnouncementQuery(announcementId, tenantId);

        var repository = Substitute.For<IAnnouncementRepository>();
        repository.GetByIdAsync(tenantId, announcementId, Arg.Any<CancellationToken>())
            .Returns((Announcement?)null);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new GetAnnouncementHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetAnnouncementValidator_WithEmptyAnnouncementId_ShouldFail()
    {
        // Arrange
        var query = new GetAnnouncementQuery(Guid.Empty, Guid.NewGuid());
        var validator = new GetAnnouncementValidator();

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetAnnouncementValidator_WithEmptyTenantId_ShouldFail()
    {
        // Arrange
        var query = new GetAnnouncementQuery(Guid.NewGuid(), Guid.Empty);
        var validator = new GetAnnouncementValidator();

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }
}
