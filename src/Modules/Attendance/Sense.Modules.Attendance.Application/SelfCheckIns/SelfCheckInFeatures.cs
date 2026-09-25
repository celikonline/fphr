using FluentValidation;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
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
        RuleFor(x => x.Direction).IsInEnum().NotEqual(PunchDirection.Unknown);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x).Must(x => (x.Latitude is null) == (x.Longitude is null)).WithMessage(AttendanceErrors.SelfCheckInCoordinatesRequired);
    }
}

public sealed class QrCheckInHandler(
    ITenantContext tenant, ICurrentUser currentUser, ILocationLookup locations, IAttendanceDayRepository days,
    IWorkScheduleReadService schedules, IShiftSettingsRepository shiftSettings, ISelfCheckInRepository selfCheckIns, ITenantLocalClock clock,
    IPunchRepository punches)
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
        var audit = SelfCheckIn.RecordQr(
            tenant.TenantId, employeeId, now.Utc, command.Direction, location.LocationId, command.Latitude, command.Longitude, geofence.Value.Within);
        var punched = await SelfCheckInPunch.ApplyAsync(
            audit, now, new SelfCheckInPunch.Ports(days, punches, schedules, shiftSettings, clock), cancellationToken);
        if (punched.IsFailure)
        {
            return punched.Error;
        }

        selfCheckIns.Add(audit);
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
        RuleFor(x => x.Direction).IsInEnum().NotEqual(PunchDirection.Unknown);
    }
}

public sealed class GpsCheckInHandler(
    ITenantContext tenant, ICurrentUser currentUser, ILocationLookup locations, IAttendanceDayRepository days,
    IWorkScheduleReadService schedules, IShiftSettingsRepository shiftSettings, ISelfCheckInRepository selfCheckIns, ITenantLocalClock clock,
    IPunchRepository punches)
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
        var audit = SelfCheckIn.RecordGps(
            tenant.TenantId, employeeId, now.Utc, command.Direction, command.LocationId, command.Latitude, command.Longitude, withinGeofence);
        var punched = await SelfCheckInPunch.ApplyAsync(
            audit, now, new SelfCheckInPunch.Ports(days, punches, schedules, shiftSettings, clock), cancellationToken);
        if (punched.IsFailure)
        {
            return punched.Error;
        }

        selfCheckIns.Add(audit);
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
/// Applies one real-time self-servis punch (S-31 kabul kriteri 4) as a raw <see cref="Punch"/> (Z0.1, Source=SelfService, SourceRef =
/// the <see cref="SelfCheckIn"/> audit row id, konum/koordinat kopyalanır) and re-derives the employee's day from its punches. Kept from
/// the pre-punch behavior: a second "In" while the day is already open (last punch is an In) is a no-op ("erken giriş kazanır"); a second
/// self-servis "Out" supersedes the previous self-servis "Out" (soft-deleted with a reason) so the latest time wins. Night shift (4857
/// md.69, same rule as the device-log import G-6-14): an "Out" with no "in" today that follows an unmatched evening "in" (12:00 or later)
/// yesterday belongs to yesterday's day. Times are the tenant's local wall-clock time, never UTC.
/// </summary>
internal static class SelfCheckInPunch
{
    public const string SupersededReason = "Sonraki self-servis çıkışıyla değiştirildi";

    private static readonly TimeOnly Noon = new(12, 0);

    public sealed record Applied(Guid DayId, DateOnly Date, TimeOnly Time);

    public sealed record Ports(
        IAttendanceDayRepository Days, IPunchRepository Punches, IWorkScheduleReadService Schedules, IShiftSettingsRepository ShiftSettings,
        ITenantLocalClock Clock);

    public static async Task<Result<Applied>> ApplyAsync(SelfCheckIn audit, TenantLocalNow now, Ports ports, CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(now.Local);
        var time = TimeOnly.FromDateTime(now.Local);
        if (audit.Direction == PunchDirection.Out)
        {
            var today = await ports.Days.FindByEmployeeAndDateAsync(audit.EmployeeId, date, cancellationToken);
            if (today?.CheckIn is null && await FindOpenOvernightDayAsync(ports.Days, audit.EmployeeId, date, cancellationToken) is { } open)
            {
                date = open.Date;
            }
        }

        var zone = await ports.Clock.GetTimeZoneAsync(audit.TenantId, cancellationToken);
        var writer = new PunchDayWriter(audit.TenantId, ports.Days, ports.Punches, ports.Schedules, zone, now.Utc);
        var dayPunches = await writer.LoadDayAsync(audit.EmployeeId, date, seedLegacy: true, cancellationToken);
        var recorded = RecordPunch(writer, audit, date, dayPunches.Count > 0 ? dayPunches[^1] : null);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        var limits = AttendanceDayUpsert.ToShiftLimits(await ports.ShiftSettings.FindAsync(cancellationToken));
        var rebuilt = await writer.RebuildAsync(audit.EmployeeId, date, AttendanceDaySource.SelfService, limits, forceIncalculable: false, cancellationToken);
        return rebuilt.IsFailure ? rebuilt.Error : new Applied(rebuilt.Value.Id, date, time);
    }

    private static Result RecordPunch(PunchDayWriter writer, SelfCheckIn audit, DateOnly workDate, Punch? last)
    {
        if (audit.Direction == PunchDirection.In && last?.Direction == PunchDirection.In)
        {
            return Result.Success();
        }

        if (audit.Direction == PunchDirection.Out && last is { Direction: PunchDirection.Out, Source: PunchSource.SelfService })
        {
            var superseded = writer.SoftDelete(last, null, SupersededReason);
            if (superseded.IsFailure)
            {
                return superseded;
            }
        }

        var added = writer.Add(
            audit.EmployeeId, audit.TimestampUtc, workDate, audit.Direction, PunchSource.SelfService, audit.Id.ToString(), null, audit.LocationId,
            audit.Latitude, audit.Longitude, false, null, null, audit.TimestampUtc);
        return added.IsFailure ? added.Error : Result.Success();
    }

    /// <summary>Yesterday's record when it holds an evening check-in (12:00+) and no check-out yet; otherwise null.</summary>
    public static async Task<AttendanceDay?> FindOpenOvernightDayAsync(
        IAttendanceDayRepository days, Guid employeeId, DateOnly today, CancellationToken cancellationToken)
    {
        var previous = await days.FindByEmployeeAndDateAsync(employeeId, today.AddDays(-1), cancellationToken);
        return previous is { CheckIn: { } checkIn, CheckOut: null } && checkIn >= Noon ? previous : null;
    }
}
