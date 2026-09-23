using NSubstitute;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Application.Enrollments;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Training.Tests;

[Trait("Category", "Integration")]
public class EnrollmentCommandsTests
{
    private static (ITenantContext Tenant, Guid TenantId) NewTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        return (tenant, tenantId);
    }

    [Fact]
    public async Task CreateEnrollment_WithNoRequirement_Succeeds()
    {
        var (tenant, tenantId) = NewTenant();
        var programId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(TrainingProgram.Create(tenantId, "Onboarding", null, TrainingCategory.Compliance, DateTime.UtcNow, null, null).Value);

        var requirements = Substitute.For<ITrainingRequirementRepository>();
        requirements.GetByTrainingProgramIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(new List<TrainingRequirement>());

        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByEmployeeIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment>());

        var handler = new CreateEnrollmentHandler(enrollments, programs, requirements, tenant);

        var result = await handler.Handle(new CreateEnrollmentCommand(programId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await enrollments.Received(1).AddAsync(Arg.Any<Enrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateEnrollment_WithUncompletedBlockingPrerequisite_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var programId = Guid.NewGuid();
        var prerequisiteId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(TrainingProgram.Create(tenantId, "Advanced", null, TrainingCategory.Technical, DateTime.UtcNow, null, null).Value);

        var requirement = TrainingRequirement.Create(tenantId, programId, prerequisiteId, blocksAssignmentUntilCompleted: true).Value;
        var requirements = Substitute.For<ITrainingRequirementRepository>();
        requirements.GetByTrainingProgramIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(new List<TrainingRequirement> { requirement });

        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByEmployeeIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment>()); // hasn't completed anything yet

        var handler = new CreateEnrollmentHandler(enrollments, programs, requirements, tenant);

        var result = await handler.Handle(new CreateEnrollmentCommand(programId, employeeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.enrollment.prerequisite_not_completed", result.Error.Code);
        await enrollments.DidNotReceive().AddAsync(Arg.Any<Enrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateEnrollment_WithCompletedBlockingPrerequisite_Succeeds()
    {
        var (tenant, tenantId) = NewTenant();
        var programId = Guid.NewGuid();
        var prerequisiteId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(TrainingProgram.Create(tenantId, "Advanced", null, TrainingCategory.Technical, DateTime.UtcNow, null, null).Value);

        var requirement = TrainingRequirement.Create(tenantId, programId, prerequisiteId, blocksAssignmentUntilCompleted: true).Value;
        var requirements = Substitute.For<ITrainingRequirementRepository>();
        requirements.GetByTrainingProgramIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(new List<TrainingRequirement> { requirement });

        var completedPrerequisite = Enrollment.Create(prerequisiteId, tenantId, employeeId).Value;
        completedPrerequisite.Complete();
        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByEmployeeIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment> { completedPrerequisite });

        var handler = new CreateEnrollmentHandler(enrollments, programs, requirements, tenant);

        var result = await handler.Handle(new CreateEnrollmentCommand(programId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await enrollments.Received(1).AddAsync(Arg.Any<Enrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateEnrollment_WithNonBlockingRequirement_IgnoresIncompletePrerequisite()
    {
        var (tenant, tenantId) = NewTenant();
        var programId = Guid.NewGuid();
        var prerequisiteId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(TrainingProgram.Create(tenantId, "Advanced", null, TrainingCategory.Technical, DateTime.UtcNow, null, null).Value);

        // Recommended, not blocking.
        var requirement = TrainingRequirement.Create(tenantId, programId, prerequisiteId, blocksAssignmentUntilCompleted: false).Value;
        var requirements = Substitute.For<ITrainingRequirementRepository>();
        requirements.GetByTrainingProgramIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(new List<TrainingRequirement> { requirement });

        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByEmployeeIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment>());

        var handler = new CreateEnrollmentHandler(enrollments, programs, requirements, tenant);

        var result = await handler.Handle(new CreateEnrollmentCommand(programId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateEnrollment_WhenEmployeeAlreadyEnrolledInProgram_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var programId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(TrainingProgram.Create(tenantId, "Onboarding", null, TrainingCategory.Compliance, DateTime.UtcNow, null, null).Value);

        var requirements = Substitute.For<ITrainingRequirementRepository>();

        var existingEnrollment = Enrollment.Create(programId, tenantId, employeeId).Value;
        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByEmployeeIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment> { existingEnrollment });

        var handler = new CreateEnrollmentHandler(enrollments, programs, requirements, tenant);

        var result = await handler.Handle(new CreateEnrollmentCommand(programId, employeeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.enrollment.duplicate", result.Error.Code);
        await enrollments.DidNotReceive().AddAsync(Arg.Any<Enrollment>(), Arg.Any<CancellationToken>());
        await requirements.DidNotReceive().GetByTrainingProgramIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateEnrollment_WithUnknownProgram_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var programs = Substitute.For<ITrainingProgramRepository>();
        var requirements = Substitute.For<ITrainingRequirementRepository>();
        var enrollments = Substitute.For<IEnrollmentRepository>();

        var handler = new CreateEnrollmentHandler(enrollments, programs, requirements, tenant);

        var result = await handler.Handle(new CreateEnrollmentCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.program.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CompleteEnrollment_ThatDoesNotExist_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var enrollments = Substitute.For<IEnrollmentRepository>();
        var handler = new CompleteEnrollmentHandler(enrollments, tenant);

        var result = await handler.Handle(new CompleteEnrollmentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.enrollment.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CompleteEnrollment_ThatExists_MarksCompleted()
    {
        var (tenant, tenantId) = NewTenant();
        var enrollment = Enrollment.Create(Guid.NewGuid(), tenantId, Guid.NewGuid()).Value;
        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByIdAsync(tenantId, enrollment.Id, Arg.Any<CancellationToken>()).Returns(enrollment);

        var handler = new CompleteEnrollmentHandler(enrollments, tenant);
        var result = await handler.Handle(new CompleteEnrollmentCommand(enrollment.Id, "CERT-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EnrollmentStatus.Completed, enrollment.Status);
        Assert.Equal("CERT-1", enrollment.CertificateNumber);
        await enrollments.Received(1).UpdateAsync(enrollment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelEnrollment_ThatExists_MarksCancelledWithoutDeleting()
    {
        var (tenant, tenantId) = NewTenant();
        var enrollment = Enrollment.Create(Guid.NewGuid(), tenantId, Guid.NewGuid()).Value;
        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByIdAsync(tenantId, enrollment.Id, Arg.Any<CancellationToken>()).Returns(enrollment);

        var handler = new CancelEnrollmentHandler(enrollments, tenant);
        var result = await handler.Handle(new CancelEnrollmentCommand(enrollment.Id, "Program tarihi değişti"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EnrollmentStatus.Cancelled, enrollment.Status);
        Assert.NotNull(enrollment.CancelledAt);
        Assert.Equal("Program tarihi değişti", enrollment.CancellationReason);
        await enrollments.Received(1).UpdateAsync(enrollment, Arg.Any<CancellationToken>());
        await enrollments.DidNotReceive().DeleteAsync(Arg.Any<Enrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelEnrollment_ThatDoesNotExist_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var enrollments = Substitute.For<IEnrollmentRepository>();
        var handler = new CancelEnrollmentHandler(enrollments, tenant);

        var result = await handler.Handle(new CancelEnrollmentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.enrollment.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CancelEnrollment_ThatIsAlreadyCancelled_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var enrollment = Enrollment.Create(Guid.NewGuid(), tenantId, Guid.NewGuid()).Value;
        enrollment.Cancel();
        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByIdAsync(tenantId, enrollment.Id, Arg.Any<CancellationToken>()).Returns(enrollment);

        var handler = new CancelEnrollmentHandler(enrollments, tenant);
        var result = await handler.Handle(new CancelEnrollmentCommand(enrollment.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.enrollment.already_cancelled", result.Error.Code);
        await enrollments.DidNotReceive().UpdateAsync(Arg.Any<Enrollment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateEnrollment_WhenPriorEnrollmentInSameProgramWasCancelled_Succeeds()
    {
        var (tenant, tenantId) = NewTenant();
        var programId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, programId, Arg.Any<CancellationToken>())
            .Returns(TrainingProgram.Create(tenantId, "Onboarding", null, TrainingCategory.Compliance, DateTime.UtcNow, null, null).Value);

        var requirements = Substitute.For<ITrainingRequirementRepository>();

        var cancelledEnrollment = Enrollment.Create(programId, tenantId, employeeId).Value;
        cancelledEnrollment.Cancel("Değişiklik oldu");
        var enrollments = Substitute.For<IEnrollmentRepository>();
        enrollments.GetByEmployeeIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment> { cancelledEnrollment });

        var handler = new CreateEnrollmentHandler(enrollments, programs, requirements, tenant);

        var result = await handler.Handle(new CreateEnrollmentCommand(programId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await enrollments.Received(1).AddAsync(Arg.Any<Enrollment>(), Arg.Any<CancellationToken>());
    }
}
