using FluentValidation;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.SelfCheckIns;

/// <summary>Result of a successful self-servis check-in/out (S-31), the caller's day snapshot after the punch was applied.</summary>
public sealed record SelfCheckInResultDto(
    Guid AttendanceDayId, DateOnly Date, PunchDirection Direction, TimeOnly Time, Guid? LocationId, string? LocationName, bool? WithinGeofence);

/// <summary>"Şu an içeride miyim" (S-31): drives which button (Giriş/Çıkış) the self check-in page shows.</summary>
public sealed record MyCheckInStatusDto(bool IsCheckedIn, DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut);

/// <summary>QR self-servis check-in (S-31 kabul kriteri 1): <paramref name="Code"/> is the Location.SelfCheckInCode scanned from its printed QR poster - physical proof of presence, no GPS needed.</summary>
[RequiresPermission(AttendancePermissions.MyCheckInKey)]
public sealed record QrCheckInCommand(string Code, PunchDirection Direction) : ICommand<SelfCheckInResultDto>;

public sealed class QrCheckInValidator : AbstractValidator<QrCheckInCommand>
{
    public QrCheckInValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Direction).IsInEnum();
    }
}

public sealed class QrCheckInHandler(
    ITenantContext tenant, ICurrentUser currentUser, ILocationLookup locations, IAttendanceDayRepository days,
    IWorkScheduleReadService schedules, ISelfCheckInRepository selfCheckIns, TimeProvider clock)
    : ICommandHandler<QrCheckInCommand, SelfCheckInResultDto>
{
    public async Task<Result<SelfCheckInResultDto>> Handle(QrCheckInCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var location = await locations.FindBySelfCheckInCodeAsync(tenant.TenantId, command.Code, cancellationToken);
        if (location is null)
        {
            return Error.NotFound(AttendanceErrors.SelfCheckInInvalidCode);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);

        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken);
        var (checkIn, checkOut) = SelfCheckInPunchMerge.Apply(existing?.CheckIn, existing?.CheckOut, command.Direction, time);

        var upserted = await AttendanceDayUpsert.UpsertAsync(
            days, schedules, tenant.TenantId, employeeId, date, checkIn, checkOut, AttendanceDaySource.SelfService, overnight: false, cancellationToken);
        if (upserted.IsFailure)
        {
            return upserted.Error;
        }

        selfCheckIns.Add(SelfCheckIn.RecordQr(tenant.TenantId, employeeId, now, command.Direction, location.LocationId));
        return new SelfCheckInResultDto(upserted.Value.Id, date, command.Direction, time, location.LocationId, location.Name, WithinGeofence: null);
    }
}

/// <summary>GPS self-servis check-in (S-31 kabul kriteri 2). <paramref name="LocationId"/> is optional - null for a roaming/remote employee with no fixed location to validate against.</summary>
[RequiresPermission(AttendancePermissions.MyCheckInKey)]
public sealed record GpsCheckInCommand(double Latitude, double Longitude, Guid? LocationId, PunchDirection Direction) : ICommand<SelfCheckInResultDto>;

public sealed class GpsCheckInValidator : AbstractValidator<GpsCheckInCommand>
{
    public GpsCheckInValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Direction).IsInEnum();
    }
}

public sealed class GpsCheckInHandler(
    ITenantContext tenant, ICurrentUser currentUser, ILocationLookup locations, IAttendanceDayRepository days,
    IWorkScheduleReadService schedules, ISelfCheckInRepository selfCheckIns, TimeProvider clock)
    : ICommandHandler<GpsCheckInCommand, SelfCheckInResultDto>
{
    public async Task<Result<SelfCheckInResultDto>> Handle(GpsCheckInCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        LocationGeofence? location = null;
        bool? withinGeofence = null;
        if (command.LocationId is { } locationId)
        {
            location = await locations.GetGeofenceAsync(tenant.TenantId, locationId, cancellationToken);
            if (location is null)
            {
                return Error.NotFound(AttendanceErrors.SelfCheckInLocationNotFound);
            }

            if (location.Latitude is { } centerLat && location.Longitude is { } centerLon && location.RadiusMeters is { } radius)
            {
                var distance = GeoDistance.Meters(command.Latitude, command.Longitude, centerLat, centerLon);
                withinGeofence = distance <= radius;
                if (withinGeofence is false)
                {
                    return Error.Validation(AttendanceErrors.SelfCheckInOutsideGeofence);
                }
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);

        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken);
        var (checkIn, checkOut) = SelfCheckInPunchMerge.Apply(existing?.CheckIn, existing?.CheckOut, command.Direction, time);

        var upserted = await AttendanceDayUpsert.UpsertAsync(
            days, schedules, tenant.TenantId, employeeId, date, checkIn, checkOut, AttendanceDaySource.SelfService, overnight: false, cancellationToken);
        if (upserted.IsFailure)
        {
            return upserted.Error;
        }

        selfCheckIns.Add(SelfCheckIn.RecordGps(tenant.TenantId, employeeId, now, command.Direction, command.LocationId, command.Latitude, command.Longitude, withinGeofence));
        return new SelfCheckInResultDto(upserted.Value.Id, date, command.Direction, time, command.LocationId, location?.Name, withinGeofence);
    }
}

/// <summary>"Şu an içeride miyim" (S-31): today's own AttendanceDay row, so the self check-in page knows which button to offer.</summary>
[RequiresPermission(AttendancePermissions.MyCheckInKey)]
public sealed record GetMyCheckInStatusQuery : IQuery<MyCheckInStatusDto>;

public sealed class GetMyCheckInStatusHandler(ICurrentUser currentUser, IAttendanceDayRepository days, TimeProvider clock)
    : IQueryHandler<GetMyCheckInStatusQuery, MyCheckInStatusDto>
{
    public async Task<Result<MyCheckInStatusDto>> Handle(GetMyCheckInStatusQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var today = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken);
        var isCheckedIn = today is { CheckIn: not null, CheckOut: null };
        return new MyCheckInStatusDto(isCheckedIn, date, today?.CheckIn, today?.CheckOut);
    }
}

/// <summary>
/// Merges one new punch into the day's existing (checkIn, checkOut) pair (S-31): an "In" only ever sets the day's
/// FIRST check-in (a second accidental "in" tap that day is a no-op, same "erken giriş kazanır" idea the PDKS
/// device-log batch import applies across a whole file); an "Out" always overwrites to the latest time (a real-time
/// single punch is, by construction, always later than anything already recorded that day).
/// </summary>
internal static class SelfCheckInPunchMerge
{
    public static (TimeOnly? CheckIn, TimeOnly? CheckOut) Apply(TimeOnly? existingCheckIn, TimeOnly? existingCheckOut, PunchDirection direction, TimeOnly time) =>
        direction == PunchDirection.In
            ? (existingCheckIn ?? time, existingCheckOut)
            : (existingCheckIn, time);
}
