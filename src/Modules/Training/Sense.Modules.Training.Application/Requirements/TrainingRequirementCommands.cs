using FluentValidation;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Requirements;

/// <summary>
/// Bir eğitim programına ön koşul tanımlar (roadmap #72: "Ön koşul eğitim").
/// <paramref name="BlocksAssignmentUntilCompleted"/> true ise ön koşul tamamlanmadan <c>CreateEnrollmentCommand</c> reddedilir.
/// </summary>
[RequiresPermission("training.programs.manage")]
public sealed record CreateTrainingRequirementCommand(
    Guid TrainingProgramId,
    Guid PrerequisiteTrainingProgramId,
    bool BlocksAssignmentUntilCompleted
) : ICommand<Guid>;

public sealed class CreateTrainingRequirementValidator : AbstractValidator<CreateTrainingRequirementCommand>
{
    public CreateTrainingRequirementValidator()
    {
        RuleFor(x => x.TrainingProgramId).NotEqual(Guid.Empty);
        RuleFor(x => x.PrerequisiteTrainingProgramId).NotEqual(Guid.Empty);
    }
}

public sealed class CreateTrainingRequirementHandler(
    ITrainingRequirementRepository requirements,
    ITrainingProgramRepository programs,
    ITenantContext tenant) : ICommandHandler<CreateTrainingRequirementCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTrainingRequirementCommand command, CancellationToken cancellationToken)
    {
        if (await programs.GetByIdAsync(tenant.TenantId, command.TrainingProgramId, cancellationToken) is null)
        {
            return TrainingErrors.ProgramNotFound;
        }

        if (await programs.GetByIdAsync(tenant.TenantId, command.PrerequisiteTrainingProgramId, cancellationToken) is null)
        {
            return TrainingErrors.ProgramNotFound;
        }

        var created = TrainingRequirement.Create(
            tenant.TenantId,
            command.TrainingProgramId,
            command.PrerequisiteTrainingProgramId,
            command.BlocksAssignmentUntilCompleted);

        if (created.IsFailure)
        {
            return created.Error;
        }

        await requirements.AddAsync(created.Value, cancellationToken);
        return created.Value.Id;
    }
}

/// <summary>Bir ön koşul kuralını kaldırır.</summary>
[RequiresPermission("training.programs.manage")]
public sealed record DeleteTrainingRequirementCommand(Guid RequirementId) : ICommand;

public sealed class DeleteTrainingRequirementHandler(ITrainingRequirementRepository requirements, ITenantContext tenant)
    : ICommandHandler<DeleteTrainingRequirementCommand>
{
    public async Task<Result> Handle(DeleteTrainingRequirementCommand command, CancellationToken cancellationToken)
    {
        var requirement = await requirements.GetByIdAsync(tenant.TenantId, command.RequirementId, cancellationToken);
        if (requirement is null)
        {
            return TrainingErrors.RequirementNotFound;
        }

        await requirements.DeleteAsync(requirement, cancellationToken);
        return Result.Success();
    }
}
