using NSubstitute;
using Sense.Shared.Contracts.Context;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Application.Programs;
using Sense.Modules.Training.Domain;
using Xunit;

namespace Sense.Modules.Training.Tests;

[Trait("Category", "Integration")]
public class TrainingProgramQueriesTests
{
    [Fact]
    public async Task GetTrainingProgramQuery_WithValidId_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var query = new GetTrainingProgramQuery(programId);

        var program = TrainingProgram.Create(
            tenantId,
            "Test Program",
            "Description",
            TrainingCategory.Technical,
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(8),
            20
        ).Value;

        var repository = Substitute.For<ITrainingProgramRepository>();
        repository.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(program);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new GetTrainingProgramHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(program.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetTrainingProgramQuery_WithInvalidId_ShouldFail()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var query = new GetTrainingProgramQuery(programId);

        var repository = Substitute.For<ITrainingProgramRepository>();
        repository.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns((TrainingProgram?)null);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new GetTrainingProgramHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetTrainingProgramValidator_WithEmptyProgramId_ShouldFail()
    {
        // Arrange
        var query = new GetTrainingProgramQuery(Guid.Empty);
        var validator = new GetTrainingProgramValidator();

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }
}
