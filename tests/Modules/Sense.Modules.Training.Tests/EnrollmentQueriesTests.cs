using NSubstitute;
using Sense.Shared.Contracts.Context;
using Sense.Modules.Training.Application.Enrollments;
using Sense.Modules.Training.Contracts;
using Sense.Modules.Training.Domain;
using Xunit;

namespace Sense.Modules.Training.Tests;

[Trait("Category", "Integration")]
public class EnrollmentQueriesTests
{
    [Fact]
    public async Task ListEnrollmentsQuery_WithValidQuery_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var query = new ListEnrollmentsQuery(tenantId, programId);

        var repository = Substitute.For<IEnrollmentRepository>();
        repository.GetByTrainingProgramIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment>());

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new ListEnrollmentsHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.IsType<List<EnrollmentDto>>(result.Value);
    }

    [Fact]
    public async Task GetEnrollmentQuery_WithValidId_ShouldSucceed()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var query = new GetEnrollmentQuery(enrollmentId, tenantId);

        var enrollment = Enrollment.Create(
            Guid.NewGuid(),
            tenantId,
            enrollmentId
        ).Value;

        var repository = Substitute.For<IEnrollmentRepository>();
        repository.GetByIdAsync(tenantId, enrollmentId, Arg.Any<CancellationToken>())
            .Returns(enrollment);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);

        var handler = new GetEnrollmentHandler(repository, tenantContext);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void ListEnrollmentsValidator_WithEmptyTenantId_ShouldFail()
    {
        // Arrange
        var query = new ListEnrollmentsQuery(Guid.Empty, Guid.NewGuid());
        var validator = new ListEnrollmentsValidator();

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetEnrollmentValidator_WithEmptyEnrollmentId_ShouldFail()
    {
        // Arrange
        var query = new GetEnrollmentQuery(Guid.Empty, Guid.NewGuid());
        var validator = new GetEnrollmentValidator();

        // Act
        var result = validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }
}
