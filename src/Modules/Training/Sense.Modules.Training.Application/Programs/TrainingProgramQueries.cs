using Sense.Shared.Contracts.Queries;
using FluentValidation;

namespace Sense.Modules.Training.Application.Programs;

public record TrainingProgramDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    int? MaxParticipants,
    int EnrolledCount
);

public record ListTrainingProgramsQuery(
    Guid TenantId,
    bool? ActiveOnly = null
) : IQuery<List<TrainingProgramDto>>;

public class ListTrainingProgramsValidator : AbstractValidator<ListTrainingProgramsQuery>
{
    public ListTrainingProgramsValidator()
    {
        // Basic validation; extend as needed
    }
}

public class ListTrainingProgramsHandler : IQueryHandler<ListTrainingProgramsQuery, List<TrainingProgramDto>>
{
    public async Task<List<TrainingProgramDto>> Handle(ListTrainingProgramsQuery request, CancellationToken cancellationToken)
    {
        // Placeholder: query implementation will be added when DbContext is available
        return new List<TrainingProgramDto>();
    }
}
