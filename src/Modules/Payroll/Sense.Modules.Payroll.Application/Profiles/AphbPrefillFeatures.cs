using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application.Incentives;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Profiles;

/// <summary>
/// The three fields APHB/e-Bildirge export prep needs (senseik-010/F-03): SGK işyeri sicil numarası (from the
/// current profile's SgkWorkplaceLocationId, via Organization.Contracts.ILocationLookup), SGK meslek kodu (from
/// Employee.Contracts.IEmployeeLookup) and teşvik kanunu kodu (the declaration code of the SGK incentive assignment in force that
/// month, else this profile's own IncentiveLawCode - see <see cref="IncentiveDeclaration"/>). The export
/// itself is out of scope (K-7 PO kararı) - this only proves the three values are queryable together, ready to be
/// pre-filled once a real APHB/e-Bildirge generator exists.
/// </summary>
public sealed record AphbPrefillDataDto(string? SgkIsyeriSicilNo, string? SgkOccupationCode, string? IncentiveLawCode);

/// <summary>The APHB/e-Bildirge pre-fill data in effect on <paramref name="AsOf"/> (defaults to today, UTC).</summary>
[RequiresPermission("payroll.profile.manage")]
public sealed record GetAphbPrefillDataQuery(Guid EmployeeId, DateOnly? AsOf = null) : IQuery<AphbPrefillDataDto>;

public sealed class GetAphbPrefillDataHandler(
    ITenantContext tenant,
    IEmployeePayrollProfileRepository profiles,
    ILocationLookup locations,
    IEmployeeLookup employees,
    IEmployeeIncentiveAssignmentRepository incentiveAssignments,
    IIncentiveDefinitionRepository incentiveDefinitions) : IQueryHandler<GetAphbPrefillDataQuery, AphbPrefillDataDto>
{
    public async Task<Result<AphbPrefillDataDto>> Handle(GetAphbPrefillDataQuery query, CancellationToken cancellationToken)
    {
        var asOf = query.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var profile = await profiles.FindCurrentAsync(query.EmployeeId, asOf, cancellationToken);

        string? sgkIsyeriSicilNo = null;
        if (profile?.SgkWorkplaceLocationId is { } locationId)
        {
            var byLocation = await locations.GetSgkIsyeriSicilNoAsync(tenant.TenantId, [locationId], cancellationToken);
            byLocation.TryGetValue(locationId, out sgkIsyeriSicilNo);
        }

        var sgkOccupationCode = await employees.GetSgkOccupationCodeAsync(tenant.TenantId, query.EmployeeId, cancellationToken);

        var incentivePlan = await IncentiveMonthPlan.LoadAsync(asOf, incentiveAssignments, incentiveDefinitions, cancellationToken);
        return new AphbPrefillDataDto(sgkIsyeriSicilNo, sgkOccupationCode, IncentiveDeclaration.CodeFor(incentivePlan, query.EmployeeId, profile?.IncentiveLawCode));
    }
}
