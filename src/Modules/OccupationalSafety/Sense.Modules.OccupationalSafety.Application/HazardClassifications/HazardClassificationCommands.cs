using FluentValidation;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Application.HazardClassifications;

/// <summary>
/// Assigns/updates a location's hazard class as of a given date (roadmap: "aylık güncelleme" - a location can be
/// reclassified at any time; this simply appends a new history row, it never mutates a past one). Rejects a
/// duplicate EffectiveFrom for the same location, mirroring Employee's SalaryRecord.AddSalaryRecord.
/// </summary>
[RequiresPermission("ohs.hazard_classifications.manage")]
public sealed record SetHazardClassificationCommand(Guid LocationId, HazardClass HazardClass, DateOnly EffectiveFrom, string? Note) : ICommand<Guid>;

public sealed class SetHazardClassificationValidator : AbstractValidator<SetHazardClassificationCommand>
{
    public SetHazardClassificationValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.HazardClass).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(OhsLimits.NoteMaxLength).When(x => x.Note is not null);
    }
}

public sealed class SetHazardClassificationHandler(ITenantContext tenant, IWorkplaceHazardClassificationRepository classifications)
    : ICommandHandler<SetHazardClassificationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SetHazardClassificationCommand command, CancellationToken cancellationToken)
    {
        var history = await classifications.ListHistoryAsync(command.LocationId, cancellationToken);
        if (history.Any(h => h.EffectiveFrom == command.EffectiveFrom))
        {
            return Error.Conflict(OhsDomainErrors.HazardClassificationEffectiveDateDuplicate);
        }

        var created = WorkplaceHazardClassification.Create(
            tenant.TenantId, command.LocationId, command.HazardClass, command.EffectiveFrom, command.Note);
        if (created.IsFailure)
        {
            return created.Error;
        }

        classifications.Add(created.Value);
        return created.Value.Id;
    }
}
