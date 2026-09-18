using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;

namespace Sense.Modules.OccupationalSafety.Application.RiskAssessments;

public sealed record RiskAssessmentDto(
    Guid Id, Guid LocationId, HazardClass HazardClassAtAssessment, DateOnly AssessmentDate,
    int RenewalPeriodMonths, DateOnly NextDueDate, string PreparedBy, string? Note);

internal static class RiskAssessmentMapper
{
    public static RiskAssessmentDto ToDto(RiskAssessment assessment) =>
        new(
            assessment.Id, assessment.LocationId, assessment.HazardClassAtAssessment, assessment.AssessmentDate,
            assessment.RenewalPeriodMonths, assessment.NextDueDate, assessment.PreparedBy, assessment.Note);
}
