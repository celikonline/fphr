using Sense.Shared.Contracts.Commands;
using FluentValidation;

namespace Sense.Modules.Training.Application.Programs;

public record CreateTrainingProgramCommand(
    Guid TenantId,
    string Title,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    int? MaxParticipants
) : ICommand<Guid>;

public class CreateTrainingProgramValidator : AbstractValidator<CreateTrainingProgramCommand>
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

public class CreateTrainingProgramHandler : ICommandHandler<CreateTrainingProgramCommand, Guid>
{
    public async Task<Guid> Handle(CreateTrainingProgramCommand request, CancellationToken cancellationToken)
    {
        // Placeholder: training program creation logic will be implemented
        // when Infrastructure/DbContext is available
        return Guid.NewGuid();
    }
}
