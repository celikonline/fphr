using NSubstitute;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Application.Requirements;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Training.Tests;

[Trait("Category", "Integration")]
public class TrainingRequirementCommandsTests
{
    private static (ITenantContext Tenant, Guid TenantId) NewTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        return (tenant, tenantId);
    }

    private static TrainingProgram AProgram(Guid tenantId, string title = "P") =>
        TrainingProgram.Create(tenantId, title, null, TrainingCategory.Technical, DateTime.UtcNow, null, null).Value;

    [Fact]
    public async Task CreateRequirement_WithBothProgramsExisting_Succeeds()
    {
        var (tenant, tenantId) = NewTenant();
        var program = AProgram(tenantId, "Advanced");
        var prerequisite = AProgram(tenantId, "Basics");

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, program.Id, Arg.Any<CancellationToken>()).Returns(program);
        programs.GetByIdAsync(tenantId, prerequisite.Id, Arg.Any<CancellationToken>()).Returns(prerequisite);

        var requirements = Substitute.For<ITrainingRequirementRepository>();
        var handler = new CreateTrainingRequirementHandler(requirements, programs, tenant);

        var result = await handler.Handle(new CreateTrainingRequirementCommand(program.Id, prerequisite.Id, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await requirements.Received(1).AddAsync(Arg.Any<TrainingRequirement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRequirement_SelfReference_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var program = AProgram(tenantId);

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, program.Id, Arg.Any<CancellationToken>()).Returns(program);

        var requirements = Substitute.For<ITrainingRequirementRepository>();
        var handler = new CreateTrainingRequirementHandler(requirements, programs, tenant);

        var result = await handler.Handle(new CreateTrainingRequirementCommand(program.Id, program.Id, true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.requirement.self_reference", result.Error.Code);
        await requirements.DidNotReceive().AddAsync(Arg.Any<TrainingRequirement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRequirement_WithUnknownPrerequisite_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var program = AProgram(tenantId);

        var programs = Substitute.For<ITrainingProgramRepository>();
        programs.GetByIdAsync(tenantId, program.Id, Arg.Any<CancellationToken>()).Returns(program);

        var requirements = Substitute.For<ITrainingRequirementRepository>();
        var handler = new CreateTrainingRequirementHandler(requirements, programs, tenant);

        var result = await handler.Handle(new CreateTrainingRequirementCommand(program.Id, Guid.NewGuid(), true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.program.not_found", result.Error.Code);
    }

    [Fact]
    public async Task DeleteRequirement_ThatExists_Removes()
    {
        var (tenant, tenantId) = NewTenant();
        var requirement = TrainingRequirement.Create(tenantId, Guid.NewGuid(), Guid.NewGuid(), true).Value;

        var requirements = Substitute.For<ITrainingRequirementRepository>();
        requirements.GetByIdAsync(tenantId, requirement.Id, Arg.Any<CancellationToken>()).Returns(requirement);

        var handler = new DeleteTrainingRequirementHandler(requirements, tenant);
        var result = await handler.Handle(new DeleteTrainingRequirementCommand(requirement.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await requirements.Received(1).DeleteAsync(requirement, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRequirement_ThatDoesNotExist_Fails()
    {
        var (tenant, tenantId) = NewTenant();
        var requirements = Substitute.For<ITrainingRequirementRepository>();
        var handler = new DeleteTrainingRequirementHandler(requirements, tenant);

        var result = await handler.Handle(new DeleteTrainingRequirementCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("training.requirement.not_found", result.Error.Code);
    }
}
