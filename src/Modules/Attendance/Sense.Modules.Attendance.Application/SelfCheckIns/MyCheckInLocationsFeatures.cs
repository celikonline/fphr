using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.SelfCheckIns;

/// <summary>
/// A location the caller can check in against (FG-005). <c>Latitude</c>/<c>Longitude</c>/<c>RadiusMeters</c> are null
/// when the location has no geofence configured - shown for information only, nothing to verify against.
/// </summary>
public sealed record CheckInLocationDto(Guid LocationId, string Name, double? Latitude, double? Longitude, int? RadiusMeters);

/// <summary>
/// "Giriş yapabileceğim lokasyonlar" (FG-005 kabul kriteri 1): Employee rolünde organization.locations.read yok, bu
/// yüzden self uç gerekiyor (Karar 2). Çalışan↔lokasyon ataması modeli yok; lokasyon çalışanın biriminin (birim → üst
/// birim zinciri) etkin lokasyonu olarak çözülür - İSG'nin OhsProfileLookup ile aynı zincir. Birimi ya da etkin
/// lokasyonu olmayan (gezici) bir çalışan boş liste alır: mobil bu durumda locationId göndermeden giriş yapar.
/// </summary>
[RequiresPermission(AttendancePermissions.MyCheckInKey)]
public sealed record GetMyCheckInLocationsQuery : IQuery<IReadOnlyList<CheckInLocationDto>>;

public sealed class GetMyCheckInLocationsHandler(
    ITenantContext tenant, ICurrentUser currentUser, IEmployeeLookup employees, IOrgUnitLookup orgUnits, ILocationLookup locations)
    : IQueryHandler<GetMyCheckInLocationsQuery, IReadOnlyList<CheckInLocationDto>>
{
    public async Task<Result<IReadOnlyList<CheckInLocationDto>>> Handle(GetMyCheckInLocationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        if (await employees.GetOrgUnitIdAsync(tenant.TenantId, employeeId, cancellationToken) is not { } orgUnitId
            || await orgUnits.GetEffectiveLocationIdAsync(tenant.TenantId, orgUnitId, cancellationToken) is not { } locationId
            || await locations.GetGeofenceAsync(tenant.TenantId, locationId, cancellationToken) is not { } geofence)
        {
            return Result.Success<IReadOnlyList<CheckInLocationDto>>([]);
        }

        return Result.Success<IReadOnlyList<CheckInLocationDto>>(
            [new CheckInLocationDto(geofence.LocationId, geofence.Name, geofence.Latitude, geofence.Longitude, geofence.RadiusMeters)]);
    }
}
