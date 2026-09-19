using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Domain.RiskAssessments;

/// <summary>
/// A completed risk assessment ("Risk Değerlendirmesi") for a <c>Location</c> (Organization module, cross-module
/// Guid reference - same pattern as <see cref="WorkplaceHazardClassification"/>). Captures the hazard class that
/// was in effect at assessment time (a later reclassification must not silently change a past assessment's
/// legally-required renewal period) and derives <see cref="NextDueDate"/> from
/// <see cref="RiskAssessmentRenewalPeriods"/> - the maximum period allowed for that hazard class (MVP: no
/// tenant-configurable shorter interval yet).
/// </summary>
public sealed class RiskAssessment : TenantAggregateRoot<Guid>, IAuditLogged
{
    private RiskAssessment()
    {
    }

    private RiskAssessment(
        Guid id, Guid tenantId, Guid locationId, HazardClass hazardClassAtAssessment, DateOnly assessmentDate,
        int renewalPeriodMonths, string preparedBy, string? note)
        : base(id, tenantId)
    {
        LocationId = locationId;
        HazardClassAtAssessment = hazardClassAtAssessment;
        AssessmentDate = assessmentDate;
        RenewalPeriodMonths = renewalPeriodMonths;
        PreparedBy = preparedBy;
        Note = note;
    }

    public Guid LocationId { get; private set; }

    /// <summary>Hazard class snapshot at assessment time - see class remarks.</summary>
    public HazardClass HazardClassAtAssessment { get; private set; }

    public DateOnly AssessmentDate { get; private set; }

    /// <summary>Months until renewal is legally due, derived from <see cref="HazardClassAtAssessment"/> at creation time.</summary>
    public int RenewalPeriodMonths { get; private set; }

    /// <summary>Assessment due date: <see cref="AssessmentDate"/> + <see cref="RenewalPeriodMonths"/>.</summary>
    public DateOnly NextDueDate => AssessmentDate.AddMonths(RenewalPeriodMonths);

    /// <summary>Free-text name of the person/firm ("İSG uzmanı", OSGB, ...) who prepared the assessment.</summary>
    public string PreparedBy { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public static Result<RiskAssessment> Create(
        Guid tenantId, Guid locationId, HazardClass hazardClassAtAssessment, DateOnly assessmentDate, string preparedBy, string? note, Guid? id = null)
    {
        if (locationId == Guid.Empty)
        {
            return Error.Validation(OhsDomainErrors.RiskAssessmentLocationRequired);
        }

        if (string.IsNullOrWhiteSpace(preparedBy))
        {
            return Error.Validation(OhsDomainErrors.RiskAssessmentPreparedByRequired);
        }

        var renewalPeriodMonths = RiskAssessmentRenewalPeriods.MonthsFor(hazardClassAtAssessment);

        return new RiskAssessment(
            id ?? Guid.CreateVersion7(),
            tenantId,
            locationId,
            hazardClassAtAssessment,
            assessmentDate,
            renewalPeriodMonths,
            Guard.MaxLength(preparedBy.Trim(), OhsLimits.PreparedByMaxLength),
            Trim(note));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), OhsLimits.NoteMaxLength);
}
