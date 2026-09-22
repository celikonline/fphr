using Sense.Modules.Employee.Contracts;
using Sense.Modules.OccupationalSafety.Contracts;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.Organization.Contracts;

namespace Sense.Modules.OccupationalSafety.Infrastructure.Lookups;

/// <summary>Implements <see cref="IOhsProfileLookup"/>: employee -> org unit -> effective location -> classification in effect.</summary>
public sealed class OhsProfileLookup(
    IEmployeeLookup employees,
    IOrgUnitLookup orgUnits,
    IWorkplaceHazardClassificationRepository classifications) : IOhsProfileLookup
{
    public async Task<OhsHazardLevel?> GetEmployeeHazardLevelAsync(
        Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (await employees.GetOrgUnitIdAsync(tenantId, employeeId, cancellationToken) is not { } orgUnitId
            || await orgUnits.GetEffectiveLocationIdAsync(tenantId, orgUnitId, cancellationToken) is not { } locationId
            || await classifications.FindCurrentAsync(locationId, asOf, cancellationToken) is not { } classification)
        {
            return null;
        }

        return classification.HazardClass switch
        {
            HazardClass.Low => OhsHazardLevel.Low,
            HazardClass.Medium => OhsHazardLevel.Medium,
            HazardClass.High => OhsHazardLevel.High,
            _ => null,
        };
    }
}
