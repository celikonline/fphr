using Sense.Shared.Contracts.Queries;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Tenants;
using Sense.Shared.Kernel.Results;
using Sense.Modules.Training.Domain;
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

public sealed class ListTrainingProgramsHandler(ITrainingProgramRepository programs, ITenantContext tenant)
    : IQueryHandler<ListTrainingProgramsQuery, List<TrainingProgramDto>>
{
    public async Task<Result<List<TrainingProgramDto>>> Handle(ListTrainingProgramsQuery request, CancellationToken cancellationToken)
    {
        var trainingPrograms = await programs.GetAllAsync(tenant.TenantId, cancellationToken);

        var dtos = trainingPrograms
            .Select(p => new TrainingProgramDto(
                p.Id,
                p.Title,
                p.Description,
                p.StartDate,
                p.EndDate,
                p.MaxParticipants,
                p.EnrolledCount
            ))
            .ToList();

        return dtos;
    }
}

/// <summary>Eğitim programını ID'ye göre getir.</summary>
[RequiresPermission("training.programs.view")]
public sealed record GetTrainingProgramQuery(
    Guid ProgramId,
    Guid TenantId
) : IQuery<TrainingProgramDto>;

public sealed class GetTrainingProgramValidator : AbstractValidator<GetTrainingProgramQuery>
{
    public GetTrainingProgramValidator()
    {
        RuleFor(x => x.ProgramId)
            .NotEqual(Guid.Empty).WithMessage("Eğitim programı gereklidir.");

        RuleFor(x => x.TenantId)
            .NotEqual(Guid.Empty).WithMessage("Kiracı gereklidir.");
    }
}

/// <summary>Eğitim programını getir — repository'den veri al.</summary>
public sealed class GetTrainingProgramHandler(ITrainingProgramRepository programs, ITenantContext tenant)
    : IQueryHandler<GetTrainingProgramQuery, TrainingProgramDto>
{
    public async Task<Result<TrainingProgramDto>> Handle(GetTrainingProgramQuery request, CancellationToken cancellationToken)
    {
        var program = await programs.GetByIdAsync(tenant.TenantId, request.ProgramId, cancellationToken);
        if (program is null)
            return TrainingErrors.ProgramNotFound;

        return new TrainingProgramDto(
            program.Id,
            program.Title,
            program.Description,
            program.StartDate,
            program.EndDate,
            program.MaxParticipants,
            program.EnrolledCount
        );
    }
}
