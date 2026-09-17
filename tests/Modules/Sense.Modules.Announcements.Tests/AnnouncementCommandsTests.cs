using NSubstitute;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

[Trait("Category", "Integration")]
public class AnnouncementCommandsTests
{
    [Fact]
    public async Task CreateAnnouncement_WithValidCommand_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var command = new CreateAnnouncementCommand(
            Title: "Test Announcement",
            Content: "This is a test announcement",
            Type: AnnouncementType.General,
            Priority: AnnouncementPriority.High
        );

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var repository = Substitute.For<IAnnouncementRepository>();
        var handler = new CreateAnnouncementHandler(tenant, repository);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        await repository.Received(1).AddAsync(Arg.Any<Announcement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateAnnouncementValidator_WithEmptyTitle_ShouldFail()
    {
        // Arrange
        var command = new CreateAnnouncementCommand(
            Title: "", // Invalid
            Content: "Some content",
            Type: AnnouncementType.General,
            Priority: AnnouncementPriority.Normal
        );

        var validator = new CreateAnnouncementValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateAnnouncementValidator_WithEmptyContent_ShouldFail()
    {
        // Arrange
        var command = new CreateAnnouncementCommand(
            Title: "Valid Title",
            Content: "", // Invalid
            Type: AnnouncementType.General,
            Priority: AnnouncementPriority.Normal
        );

        var validator = new CreateAnnouncementValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
