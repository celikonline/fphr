using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;
using Sense.Shared.Kernel.Results;

using Sense.Modules.OccupationalSafety.Domain.Incidents;

namespace Sense.Modules.OccupationalSafety.Domain;

/// <summary>WorkplaceHazardClassification repository.</summary>
public interface IWorkplaceHazardClassificationRepository
{
    void Add(WorkplaceHazardClassification classification);

    /// <summary>The entry in effect on <paramref name="asOf"/> for the location: the latest EffectiveFrom that has started; null when none has.</summary>
    Task<WorkplaceHazardClassification?> FindCurrentAsync(Guid locationId, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>Full history for a location, newest EffectiveFrom first.</summary>
    Task<IReadOnlyList<WorkplaceHazardClassification>> ListHistoryAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>The classification in effect on <paramref name="asOf"/> for every location that has one, one row per location.</summary>
    Task<IReadOnlyList<WorkplaceHazardClassification>> ListCurrentForAllLocationsAsync(DateOnly asOf, CancellationToken cancellationToken = default);
}

/// <summary>OhsIncident (iş kazası / ramak kala) repository.</summary>
public interface IOhsIncidentRepository
{
    void Add(OhsIncident incident);

    Task<Result<OhsIncident>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All incidents, newest EventDate first.</summary>
    Task<IReadOnlyList<OhsIncident>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Open work accidents not yet notified to SGK (the daily reminder's input).</summary>
    Task<IReadOnlyList<OhsIncident>> ListAwaitingSgkNotificationAsync(CancellationToken cancellationToken = default);

    /// <summary>Incidents the employee reported through self-service, newest first.</summary>
    Task<IReadOnlyList<OhsIncident>> ListReportedByAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

/// <summary>RiskAssessment repository.</summary>
public interface IRiskAssessmentRepository
{
    void Add(RiskAssessment assessment);

    Task<Result<RiskAssessment>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskAssessment>> ListByLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>All assessments, newest AssessmentDate first (settings page: one flat list across locations).</summary>
    Task<IReadOnlyList<RiskAssessment>> ListAsync(CancellationToken cancellationToken = default);
}
