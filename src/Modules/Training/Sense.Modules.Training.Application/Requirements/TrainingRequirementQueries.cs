using Sense.Modules.Training.Contracts;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Application.Requirements;

/// <summary>Bir programa ait ön koşul kurallarını listeler.</summary>
[RequiresPermission("training.programs.view")]
public sealed record ListTrainingRequirementsQuery(Guid TrainingProgramId) : IQuery<List<TrainingRequirementDto>>;

public sealed class ListTrainingRequirementsHandler(ITrainingRequirementRepository requirements, ITenantContext tenant)
    : IQueryHandler<ListTrainingRequirementsQuery, List<TrainingRequirementDto>>
{
    public async Task<Result<List<TrainingRequirementDto>>> Handle(ListTrainingRequirementsQuery request, CancellationToken cancellationToken)
    {
        var rows = await requirements.GetByTrainingProgramIdAsync(tenant.TenantId, request.TrainingProgramId, cancellationToken);
        return rows
            .Select(r => new TrainingRequirementDto(r.Id, r.TrainingProgramId, r.PrerequisiteTrainingProgramId, r.BlocksAssignmentUntilCompleted))
            .ToList();
    }
}
