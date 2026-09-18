using FluentValidation;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Commands;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Programs;

/// <summary>Eğitim programı oluştur (İK tarafından).</summary>
[RequiresPermission("training.programs.manage")]
public sealed record CreateTrainingProgramCommand(
    string Title,
    string? Description,
    TrainingCategory Category,
    DateTime StartDate,
    DateTime EndDate,
    int? MaxCapacity
) : ICommand<Guid>;

public sealed class CreateTrainingProgramValidator : AbstractValidator<CreateTrainingProgramCommand>
{
    public CreateTrainingProgramValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode(TrainingErrors.TitleRequired.Code);

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .WithErrorCode(TrainingErrors.InvalidDateRange.Code);
    }
}

/// <summary>Eğitim programı oluştur — İK/HR tarafından (training.programs.manage izni gerekir).</summary>
public sealed class CreateTrainingProgramHandler(ITenantContext tenant, ITrainingProgramRepository programs)
    : ICommandHandler<CreateTrainingProgramCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTrainingProgramCommand command, CancellationToken cancellationToken)
    {
        var created = TrainingProgram.Create(
            tenant.TenantId,
            command.Title,
            command.Description,
            command.Category,
            command.StartDate,
            command.EndDate,
            command.MaxCapacity);

        if (created.IsFailure)
        {
            return created.Error;
        }

        await programs.AddAsync(created.Value, cancellationToken);
        return created.Value.Id;
    }
}
