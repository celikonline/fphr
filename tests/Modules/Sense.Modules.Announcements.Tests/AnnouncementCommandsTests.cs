using Xunit;
using Sense.Modules.Announcements.Application.Announcements;

namespace Sense.Modules.Announcements.Tests;

[Trait("Category", "Integration")]
public class AnnouncementCommandsTests
{
    [Fact]
    public async Task CreateAnnouncement_WithValidCommand_ShouldSucceed()
    {
        // Arrange
        var command = new CreateAnnouncementCommand(
            TenantId: Guid.NewGuid(),
            Title: "Test Announcement",
            Content: "This is a test announcement"
        );

        var handler = new CreateAnnouncementHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, result);
    }

    [Fact]
    public void CreateAnnouncementValidator_WithEmptyTitle_ShouldFail()
    {
        // Arrange
        var command = new CreateAnnouncementCommand(
            TenantId: Guid.NewGuid(),
            Title: "", // Invalid
            Content: "Some content"
        );

        var validator = new CreateAnnouncementValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
