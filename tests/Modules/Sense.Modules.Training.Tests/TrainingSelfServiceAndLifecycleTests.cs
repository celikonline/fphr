using NSubstitute;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Application.Enrollments;
using Sense.Modules.Training.Application.Programs;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Training.Tests;

public class TrainingSelfServiceAndLifecycleTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ITrainingProgramRepository _programs = Substitute.For<ITrainingProgramRepository>();
    private readonly IEnrollmentRepository _enrollments = Substitute.For<IEnrollmentRepository>();
    private readonly ITrainingRequirementRepository _requirements = Substitute.For<ITrainingRequirementRepository>();

    public TrainingSelfServiceAndLifecycleTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _requirements.GetByTrainingProgramIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<TrainingRequirement>());
        _enrollments.GetByEmployeeIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<Enrollment>());
    }

    private TrainingProgram Program(string title, DateTime start)
    {
        var program = TrainingProgram.Create(_tenantId, title, null, TrainingCategory.Compliance, start, start.AddDays(1), null).Value;
        _programs.GetByIdAsync(_tenantId, program.Id, Arg.Any<CancellationToken>()).Returns(program);
        return program;
    }

    [Fact]
    public async Task Activate_DraftProgram_BecomesActiveAndIsSaved()
    {
        var program = Program("İSG temel", DateTime.UtcNow);

        var result = await new ActivateTrainingProgramHandler(_tenant, _programs)
            .Handle(new ActivateTrainingProgramCommand(program.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TrainingStatus.Active, program.Status);
        await _programs.Received(1).UpdateAsync(program, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activate_AlreadyActiveProgram_FailsWithoutSaving()
    {
        var program = Program("İSG temel", DateTime.UtcNow);
        program.Activate();

        var result = await new ActivateTrainingProgramHandler(_tenant, _programs)
            .Handle(new ActivateTrainingProgramCommand(program.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        await _programs.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Close_UnknownProgram_ReturnsNotFound()
    {
        var result = await new CloseTrainingProgramHandler(_tenant, _programs)
            .Handle(new CloseTrainingProgramCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(TrainingErrors.ProgramNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task CreateEnrollment_InClosedProgram_IsRejected()
    {
        var program = Program("Kapanan", DateTime.UtcNow);
        program.Close();

        var result = await new CreateEnrollmentHandler(_enrollments, _programs, _requirements, _tenant)
            .Handle(new CreateEnrollmentCommand(program.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(TrainingErrors.ProgramNotOpen.Code, result.Error.Code);
        await _enrollments.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task ListMyTrainings_ReturnsOwnEnrollmentsWithProgramDetails_NewestProgramFirst()
    {
        var employeeId = Guid.NewGuid();
        var older = Program("Yangın eğitimi", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Program("KVKK farkındalık", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        _programs.GetAllAsync(_tenantId, Arg.Any<CancellationToken>()).Returns([older, newer]);
        var completed = Enrollment.Create(older.Id, _tenantId, employeeId).Value;
        completed.Complete("CERT-1");
        _enrollments.GetByEmployeeIdAsync(_tenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns([completed, Enrollment.Create(newer.Id, _tenantId, employeeId).Value]);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var result = await new ListMyTrainingsHandler(_enrollments, _programs, _tenant, currentUser)
            .Handle(new ListMyTrainingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["KVKK farkındalık", "Yangın eğitimi"], result.Value.Select(t => t.Title));
        Assert.Equal("Completed", result.Value[1].Status);
        Assert.Equal("CERT-1", result.Value[1].CertificateNumber);
    }

    [Fact]
    public async Task ListMyTrainings_WithoutEmployeeRecord_IsEmpty()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);

        var result = await new ListMyTrainingsHandler(_enrollments, _programs, _tenant, currentUser)
            .Handle(new ListMyTrainingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
