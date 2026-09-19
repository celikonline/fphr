using FluentValidation;
using Sense.Modules.Training.Contracts;
using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Programs;

/// <summary>Eğitim programlarını listele.</summary>
[RequiresPermission("training.programs.view")]
public sealed record ListTrainingProgramsQuery(bool? ActiveOnly = null) : IQuery<List<TrainingProgramDto>>;

/// <summary>Eğitim programlarını listele — repository'den veri al.</summary>
public sealed class ListTrainingProgramsHandler(ITrainingProgramRepository programs, ITenantContext tenant)
    : IQueryHandler<ListTrainingProgramsQuery, List<TrainingProgramDto>>
{
    public async Task<Result<List<TrainingProgramDto>>> Handle(ListTrainingProgramsQuery request, CancellationToken cancellationToken)
    {
        var trainingPrograms = await programs.GetAllAsync(tenant.TenantId, cancellationToken);

        var filtered = request.ActiveOnly is true
            ? trainingPrograms.Where(p => p.Status == TrainingStatus.Active)
            : trainingPrograms;

        return filtered.Select(TrainingProgramMapping.ToDto).ToList();
    }
}

/// <summary>Eğitim programını ID'ye göre getir.</summary>
[RequiresPermission("training.programs.view")]
public sealed record GetTrainingProgramQuery(Guid ProgramId) : IQuery<TrainingProgramDto>;

public sealed class GetTrainingProgramValidator : AbstractValidator<GetTrainingProgramQuery>
{
    public GetTrainingProgramValidator()
    {
        RuleFor(x => x.ProgramId).NotEqual(Guid.Empty).WithMessage("Eğitim programı gereklidir.");
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
        {
            return TrainingErrors.ProgramNotFound;
        }

        return TrainingProgramMapping.ToDto(program);
    }
}

internal static class TrainingProgramMapping
{
    public static TrainingProgramDto ToDto(TrainingProgram p) => new(
        p.Id,
        p.Title,
        p.Description,
        p.Category.ToString(),
        p.StartDate,
        p.EndDate,
        p.MaxCapacity,
        p.Status.ToString());
}
