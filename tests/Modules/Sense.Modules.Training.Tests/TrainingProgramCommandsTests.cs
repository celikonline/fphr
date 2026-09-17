using Xunit;
using Sense.Modules.Training.Application.Programs;

namespace Sense.Modules.Training.Tests;

[Trait("Category", "Integration")]
public class TrainingProgramCommandsTests
{
    [Fact]
    public async Task CreateTrainingProgram_WithValidCommand_ShouldSucceed()
    {
        // Arrange
        var command = new CreateTrainingProgramCommand(
            TenantId: Guid.NewGuid(),
            Title: "Test Training Program",
            Description: "A test program",
            StartDate: DateTime.UtcNow.AddDays(1),
            EndDate: DateTime.UtcNow.AddDays(8)
        );

        var handler = new CreateTrainingProgramHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, result);
    }

    [Fact]
    public void CreateTrainingProgramValidator_WithEmptyTitle_ShouldFail()
    {
        // Arrange
        var command = new CreateTrainingProgramCommand(
            TenantId: Guid.NewGuid(),
            Title: "", // Invalid
            Description: null,
            StartDate: DateTime.UtcNow.AddDays(1),
            EndDate: DateTime.UtcNow.AddDays(8)
        );

        var validator = new CreateTrainingProgramValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
