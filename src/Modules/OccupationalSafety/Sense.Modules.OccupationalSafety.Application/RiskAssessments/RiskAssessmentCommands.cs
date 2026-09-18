using FluentValidation;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Application.RiskAssessments;

/// <summary>
/// Records a completed risk assessment for a location. HazardClass is deliberately not a command input: it is
/// snapshotted from the location's WorkplaceHazardClassification in effect on AssessmentDate (RiskAssessment.cs
/// remarks) so a risk assessment can never be recorded against a class the location was not actually assigned to
/// - fails with RiskAssessmentHazardClassificationMissing when the location has no classification yet.
/// </summary>
[RequiresPermission("ohs.risk_assessments.manage")]
public sealed record RecordRiskAssessmentCommand(Guid LocationId, DateOnly AssessmentDate, string PreparedBy, string? Note) : ICommand<Guid>;

public sealed class RecordRiskAssessmentValidator : AbstractValidator<RecordRiskAssessmentCommand>
{
    public RecordRiskAssessmentValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.PreparedBy).NotEmpty().MaximumLength(OhsLimits.PreparedByMaxLength);
        RuleFor(x => x.Note).MaximumLength(OhsLimits.NoteMaxLength).When(x => x.Note is not null);
    }
}

public sealed class RecordRiskAssessmentHandler(
    ITenantContext tenant, IRiskAssessmentRepository assessments, IWorkplaceHazardClassificationRepository classifications)
    : ICommandHandler<RecordRiskAssessmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordRiskAssessmentCommand command, CancellationToken cancellationToken)
    {
        var classification = await classifications.FindCurrentAsync(command.LocationId, command.AssessmentDate, cancellationToken);
        if (classification is null)
        {
            return Error.Rule(OhsDomainErrors.RiskAssessmentHazardClassificationMissing);
        }

        var created = RiskAssessment.Create(
            tenant.TenantId, command.LocationId, classification.HazardClass, command.AssessmentDate, command.PreparedBy, command.Note);
        if (created.IsFailure)
        {
            return created.Error;
        }

        assessments.Add(created.Value);
        return created.Value.Id;
    }
}
