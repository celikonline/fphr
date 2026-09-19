using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Domain.HazardClassifications;

/// <summary>
/// One entry of a <c>Location</c>'s (Organization module, cross-module Guid reference - same pattern as
/// Assets.AssignedEmployeeId) hazard-class history: in effect from <see cref="EffectiveFrom"/> until the next
/// record for that location starts (mirrors Employee's SalaryRecord "history, no EffectiveTo" convention -
/// current-record resolution is "latest EffectiveFrom that has started"). Standalone aggregate root (not a
/// child entity) because Location lives in a different module/schema (ADR 0003) - this module holds only its
/// Guid, so it cannot be an EF-owned child of Location the way SalaryRecord is owned by Employee.
/// </summary>
public sealed class WorkplaceHazardClassification : TenantAggregateRoot<Guid>, IAuditLogged
{
    private WorkplaceHazardClassification()
    {
    }

    private WorkplaceHazardClassification(Guid id, Guid tenantId, Guid locationId, HazardClass hazardClass, DateOnly effectiveFrom, string? note)
        : base(id, tenantId)
    {
        LocationId = locationId;
        HazardClass = hazardClass;
        EffectiveFrom = effectiveFrom;
        Note = note;
    }

    public Guid LocationId { get; private set; }

    public HazardClass HazardClass { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public string? Note { get; private set; }

    public static Result<WorkplaceHazardClassification> Create(
        Guid tenantId, Guid locationId, HazardClass hazardClass, DateOnly effectiveFrom, string? note, Guid? id = null)
    {
        if (locationId == Guid.Empty)
        {
            return Error.Validation(OhsDomainErrors.HazardClassificationLocationRequired);
        }

        if (!Enum.IsDefined(hazardClass))
        {
            return Error.Validation(OhsDomainErrors.HazardClassificationInvalid);
        }

        return new WorkplaceHazardClassification(
            id ?? Guid.CreateVersion7(),
            tenantId,
            locationId,
            hazardClass,
            effectiveFrom,
            Trim(note));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), OhsLimits.NoteMaxLength);
}
