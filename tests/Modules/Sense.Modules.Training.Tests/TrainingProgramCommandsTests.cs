using NSubstitute;
using Sense.Modules.Training.Application.Programs;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Training.Tests;

[Trait("Category", "Integration")]
public class TrainingProgramCommandsTests
{
    [Fact]
    public async Task CreateTrainingProgram_WithValidCommand_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var command = new CreateTrainingProgramCommand(
            Title: "Test Training Program",
            Description: "A test program",
            Category: TrainingCategory.Technical,
            StartDate: DateTime.UtcNow.AddDays(1),
            EndDate: DateTime.UtcNow.AddDays(8),
            MaxCapacity: 20
        );

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);

        var repository = Substitute.For<ITrainingProgramRepository>();
        var handler = new CreateTrainingProgramHandler(tenant, repository);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        await repository.Received(1).AddAsync(Arg.Any<TrainingProgram>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateTrainingProgramValidator_WithEmptyTitle_ShouldFail()
    {
        // Arrange
        var command = new CreateTrainingProgramCommand(
            Title: "", // Invalid
            Description: null,
            Category: TrainingCategory.Technical,
            StartDate: DateTime.UtcNow.AddDays(1),
            EndDate: DateTime.UtcNow.AddDays(8),
            MaxCapacity: null
        );

        var validator = new CreateTrainingProgramValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateTrainingProgramValidator_WithInvalidDateRange_ShouldFail()
    {
        // Arrange
        var command = new CreateTrainingProgramCommand(
            Title: "Valid Title",
            Description: null,
            Category: TrainingCategory.Compliance,
            StartDate: DateTime.UtcNow.AddDays(8),
            EndDate: DateTime.UtcNow.AddDays(1), // End before start
            MaxCapacity: null
        );

        var validator = new CreateTrainingProgramValidator();

        // Act
        var result = validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
