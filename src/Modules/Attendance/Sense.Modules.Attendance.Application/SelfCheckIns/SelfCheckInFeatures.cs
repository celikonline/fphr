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

/// <summary>
/// QR self-servis check-in (S-31 kabul kriteri 1): <paramref name="Code"/> is the Location.SelfCheckInCode scanned from its
/// printed QR poster. The poster is a static secret, so when the location has a geofence radius the caller's coordinates
/// are REQUIRED and validated server-side too - a photo of the poster forwarded to someone off-site is not enough
/// (<paramref name="Latitude"/>/<paramref name="Longitude"/> are only recorded for locations without a radius).
/// </summary>
[RequiresPermission(AttendancePermissions.MyCheckInKey)]
public sealed record QrCheckInCommand(string Code, PunchDirection Direction, double? Latitude = null, double? Longitude = null) : ICommand<SelfCheckInResultDto>;

public sealed class QrCheckInValidator : AbstractValidator<QrCheckInCommand>
{
    public QrCheckInValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x).Must(x => (x.Latitude is null) == (x.Longitude is null)).WithMessage(AttendanceErrors.SelfCheckInCoordinatesRequired);
    }
}

public sealed class QrCheckInHandler(
    ITenantContext tenant, ICurrentUser currentUser, ILocationLookup locations, IAttendanceDayRepository days,
    IWorkScheduleReadService schedules, IShiftSettingsRepository shiftSettings, ISelfCheckInRepository selfCheckIns, ITenantLocalClock clock)
    : ICommandHandler<QrCheckInCommand, SelfCheckInResultDto>
{
    public async Task<Result<SelfCheckInResultDto>> Handle(QrCheckInCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        // Tenant-scoped exact-match lookup of a 12-char CSPRNG code: an unknown, foreign-tenant or rotated code all look the same (invalid_code).
        var location = await locations.FindBySelfCheckInCodeAsync(tenant.TenantId, command.Code.Trim(), cancellationToken);
        if (location is null)
        {
            return Error.NotFound(AttendanceErrors.SelfCheckInInvalidCode);
        }

        var geofence = SelfCheckInGeofence.Evaluate(location, command.Latitude, command.Longitude);
        if (geofence.IsFailure)
        {
            return geofence.Error;
        }

        var now = await clock.NowAsync(tenant.TenantId, cancellationToken);
        var punched = await SelfCheckInPunch.ApplyAsync(
            tenant.TenantId, employeeId, command.Direction, now, days, schedules, shiftSettings, cancellationToken);
        if (punched.IsFailure)
        {
            return punched.Error;
        }

        selfCheckIns.Add(SelfCheckIn.RecordQr(
            tenant.TenantId, employeeId, now.Utc, command.Direction, location.LocationId, command.Latitude, command.Longitude, geofence.Value.Within));
        return new SelfCheckInResultDto(punched.Value.DayId, punched.Value.Date, command.Direction, punched.Value.Time, location.LocationId, location.Name, geofence.Value.Within);
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
    IWorkScheduleReadService schedules, IShiftSettingsRepository shiftSettings, ISelfCheckInRepository selfCheckIns, ITenantLocalClock clock)
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

            var geofence = SelfCheckInGeofence.Evaluate(location, command.Latitude, command.Longitude);
            if (geofence.IsFailure)
            {
                return geofence.Error;
            }

            withinGeofence = geofence.Value.Within;
        }

        var now = await clock.NowAsync(tenant.TenantId, cancellationToken);
        var punched = await SelfCheckInPunch.ApplyAsync(
            tenant.TenantId, employeeId, command.Direction, now, days, schedules, shiftSettings, cancellationToken);
        if (punched.IsFailure)
        {
            return punched.Error;
        }

        selfCheckIns.Add(SelfCheckIn.RecordGps(
            tenant.TenantId, employeeId, now.Utc, command.Direction, command.LocationId, command.Latitude, command.Longitude, withinGeofence));
        return new SelfCheckInResultDto(punched.Value.DayId, punched.Value.Date, command.Direction, punched.Value.Time, command.LocationId, location?.Name, withinGeofence);
    }
}

/// <summary>"Şu an içeride miyim" (S-31): the caller's open (or today's) AttendanceDay row, so the self check-in page knows which button to offer.</summary>
[RequiresPermission(AttendancePermissions.MyCheckInKey)]
public sealed record GetMyCheckInStatusQuery : IQuery<MyCheckInStatusDto>;

public sealed class GetMyCheckInStatusHandler(
    ITenantContext tenant, ICurrentUser currentUser, IAttendanceDayRepository days, ITenantLocalClock clock)
    : IQueryHandler<GetMyCheckInStatusQuery, MyCheckInStatusDto>
{
    public async Task<Result<MyCheckInStatusDto>> Handle(GetMyCheckInStatusQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var now = await clock.NowAsync(tenant.TenantId, cancellationToken);
        var today = DateOnly.FromDateTime(now.Local);
        var record = await days.FindByEmployeeAndDateAsync(employeeId, today, cancellationToken);
        if (record?.CheckIn is null)
        {
            // Night shift: an evening "in" yesterday with no "out" yet is still open this morning.
            var open = await SelfCheckInPunch.FindOpenOvernightDayAsync(days, employeeId, today, cancellationToken);
            if (open is not null)
            {
                return new MyCheckInStatusDto(IsCheckedIn: true, open.Date, open.CheckIn, open.CheckOut);
            }
        }

        var isCheckedIn = record is { CheckIn: not null, CheckOut: null };
        return new MyCheckInStatusDto(isCheckedIn, today, record?.CheckIn, record?.CheckOut);
    }
}

/// <summary>
/// Server-side geofence verdict (S-31): never trusts a client-side "I'm inside" claim. A location without a radius is not
/// checked (null; coordinates are only recorded). With a radius, missing coordinates are rejected and the Haversine
/// distance must be within the radius.
/// </summary>
/// <summary>Geofence outcome: <c>Within</c> is null when the location has no radius (nothing to verify). Wrapped because a null <c>Result&lt;bool?&gt;</c> value would read as a failure.</summary>
internal readonly record struct GeofenceVerdict(bool? Within);

internal static class SelfCheckInGeofence
{
    public static Result<GeofenceVerdict> Evaluate(LocationGeofence location, double? latitude, double? longitude)
    {
        if (location.Latitude is not { } centerLat || location.Longitude is not { } centerLon || location.RadiusMeters is not { } radius)
        {
            return new GeofenceVerdict(null);
        }

        if (latitude is not { } lat || longitude is not { } lon)
        {
            return Error.Validation(AttendanceErrors.SelfCheckInCoordinatesRequired);
        }

        return GeoDistance.Meters(lat, lon, centerLat, centerLon) <= radius
            ? new GeofenceVerdict(true)
            : Error.Validation(AttendanceErrors.SelfCheckInOutsideGeofence);
    }
}

/// <summary>
/// Applies one real-time punch to the employee's day through the shared <see cref="AttendanceDayUpsert"/> (S-31 kabul
/// kriteri 4). An "In" only ever sets the day's FIRST check-in (a second accidental tap that day is a no-op, same
/// "erken giriş kazanır" idea the PDKS device-log batch import applies); an "Out" always overwrites to the latest time.
/// Night shift (4857 md.69, same rule as the device-log import G-6-14): an "Out" with no "in" today that follows an
/// unmatched evening "in" (12:00 or later) yesterday closes yesterday's record as overnight. Times are the tenant's
/// local wall-clock time (the work schedule the day is compared against is local too), never UTC.
/// </summary>
internal static class SelfCheckInPunch
{
    private static readonly TimeOnly Noon = new(12, 0);

    public sealed record Applied(Guid DayId, DateOnly Date, TimeOnly Time);

    public static async Task<Result<Applied>> ApplyAsync(
        Guid tenantId, Guid employeeId, PunchDirection direction, TenantLocalNow now, IAttendanceDayRepository days,
        IWorkScheduleReadService schedules, IShiftSettingsRepository shiftSettings, CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(now.Local);
        var time = TimeOnly.FromDateTime(now.Local);
        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken);
        var overnight = false;

        if (direction == PunchDirection.Out && existing?.CheckIn is null)
        {
            var open = await FindOpenOvernightDayAsync(days, employeeId, date, cancellationToken);
            if (open is not null)
            {
                existing = open;
                date = open.Date;
                overnight = true;
            }
        }

        var (checkIn, checkOut) = Merge(existing?.CheckIn, existing?.CheckOut, direction, time);
        var limits = AttendanceDayUpsert.ToShiftLimits(await shiftSettings.FindAsync(cancellationToken));
        var upserted = await AttendanceDayUpsert.UpsertAsync(
            days, schedules, tenantId, employeeId, date, checkIn, checkOut, AttendanceDaySource.SelfService, overnight, cancellationToken,
            existing?.ActualBreakMinutes ?? 0, limits);
        return upserted.IsFailure ? upserted.Error : new Applied(upserted.Value.Id, date, time);
    }

    /// <summary>Yesterday's record when it holds an evening check-in (12:00+) and no check-out yet; otherwise null.</summary>
    public static async Task<AttendanceDay?> FindOpenOvernightDayAsync(
        IAttendanceDayRepository days, Guid employeeId, DateOnly today, CancellationToken cancellationToken)
    {
        var previous = await days.FindByEmployeeAndDateAsync(employeeId, today.AddDays(-1), cancellationToken);
        return previous is { CheckIn: { } checkIn, CheckOut: null } && checkIn >= Noon ? previous : null;
    }

    internal static (TimeOnly? CheckIn, TimeOnly? CheckOut) Merge(TimeOnly? existingCheckIn, TimeOnly? existingCheckOut, PunchDirection direction, TimeOnly time) =>
        direction == PunchDirection.In
            ? (existingCheckIn ?? time, existingCheckOut)
            : (existingCheckIn, time);
}
