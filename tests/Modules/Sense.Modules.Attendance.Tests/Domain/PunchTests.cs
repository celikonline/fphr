using System.Globalization;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Domain;

/// <summary>Ham okutma (Z0.1, spec §4 <c>Punch</c>): oluşturma kuralları ve gerekçeli yumuşak silme.</summary>
public sealed class PunchTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Emp = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void Create_rejects_future_timestamp_beyond_tolerance()
    {
        var now = T("2026-09-01T06:00Z");

        var result = Punch.Create(
            Tenant, Emp, now.AddMinutes(10), new DateOnly(2026, 9, 1), PunchDirection.In, PunchSource.Device, null, null, null, null, null, false, null, null, now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.PunchInFuture);
    }

    [Fact]
    public void Create_rejects_future_timestamp_against_system_clock_when_now_is_not_given()
    {
        var future = DateTime.UtcNow.AddMinutes(10);

        var result = Punch.Create(
            Tenant, Emp, future, DateOnly.FromDateTime(future), PunchDirection.In, PunchSource.Device, null, null, null, null, null, false, null, null);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.PunchInFuture);
    }

    [Fact]
    public void Create_accepts_small_clock_skew_within_tolerance()
    {
        var now = T("2026-09-01T06:00Z");

        var result = Punch.Create(
            Tenant, Emp, now.AddMinutes(Punch.FutureToleranceMinutes), new DateOnly(2026, 9, 1), PunchDirection.In, PunchSource.Terminal, null, null, null, null, null,
            false, null, null, now);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_keeps_every_field_and_starts_not_deleted()
    {
        var terminalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var punch = Punch.Create(
            Tenant, Emp, T("2026-09-01T21:30Z"), new DateOnly(2026, 9, 1), PunchDirection.Out, PunchSource.Terminal, "  job-42  ", terminalId, locationId, 41.0, 29.0,
            false, User, "  cihaz  ").Value;

        punch.TenantId.ShouldBe(Tenant);
        punch.EmployeeId.ShouldBe(Emp);
        punch.OccurredAtUtc.ShouldBe(T("2026-09-01T21:30Z"));
        punch.OccurredAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
        punch.WorkDate.ShouldBe(new DateOnly(2026, 9, 1));
        punch.Direction.ShouldBe(PunchDirection.Out);
        punch.Source.ShouldBe(PunchSource.Terminal);
        punch.SourceRef.ShouldBe("job-42");
        punch.TerminalId.ShouldBe(terminalId);
        punch.LocationId.ShouldBe(locationId);
        punch.Latitude.ShouldBe(41.0);
        punch.Longitude.ShouldBe(29.0);
        punch.IsManual.ShouldBeFalse();
        punch.CreatedByUserId.ShouldBe(User);
        punch.Reason.ShouldBe("cihaz");
        punch.IsDeleted.ShouldBeFalse();
        punch.DeletedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Create_treats_unspecified_kind_as_utc()
    {
        var unspecified = new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Unspecified);

        var punch = Punch.Create(
            Tenant, Emp, unspecified, new DateOnly(2026, 9, 1), PunchDirection.Unknown, PunchSource.Import, null, null, null, null, null, false, null, null).Value;

        punch.OccurredAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
        punch.OccurredAtUtc.ShouldBe(T("2026-09-01T06:00Z"));
        punch.Direction.ShouldBe(PunchDirection.Unknown);
    }

    [Fact]
    public void Create_rejects_empty_employee()
    {
        var result = Punch.Create(
            Tenant, Guid.Empty, T("2026-09-01T06:00Z"), new DateOnly(2026, 9, 1), PunchDirection.In, PunchSource.Manual, null, null, null, null, null, true, User, null);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.PunchEmployeeRequired);
    }

    [Theory]
    [InlineData(41.0, null)]
    [InlineData(null, 29.0)]
    [InlineData(91.0, 29.0)]
    [InlineData(41.0, -181.0)]
    public void Create_rejects_partial_or_out_of_range_coordinates(double? lat, double? lon)
    {
        var result = Punch.Create(
            Tenant, Emp, T("2026-09-01T06:00Z"), new DateOnly(2026, 9, 1), PunchDirection.In, PunchSource.SelfService, null, null, null, lat, lon, false, null, null);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.PunchCoordinatesInvalid);
    }

    [Theory]
    [InlineData(2026, 8, 30)]
    [InlineData(2026, 9, 3)]
    public void Create_rejects_work_date_more_than_a_day_away_from_the_timestamp(int year, int month, int day)
    {
        var result = Punch.Create(
            Tenant, Emp, T("2026-09-01T06:00Z"), new DateOnly(year, month, day), PunchDirection.In, PunchSource.Manual, null, null, null, null, null, true, User, null);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.PunchWorkDateInvalid);
    }

    [Fact]
    public void Create_accepts_previous_day_work_date_for_a_night_shift_checkout()
    {
        // 22:00-06:00 gece vardiyası: 05:50 çıkışı vardiyanın başladığı güne (bir önceki takvim günü) yazılır.
        var result = Punch.Create(
            Tenant, Emp, T("2026-09-02T02:50Z"), new DateOnly(2026, 9, 1), PunchDirection.Out, PunchSource.Device, null, null, null, null, null, false, null, null);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_rejects_reason_longer_than_limit()
    {
        var result = Punch.Create(
            Tenant, Emp, T("2026-09-01T06:00Z"), new DateOnly(2026, 9, 1), PunchDirection.In, PunchSource.Manual, null, null, null, null, null, true, User,
            new string('x', Punch.ReasonMaxLength + 1));

        result.Error.Code.ShouldBe(AttendanceDomainErrors.PunchReasonTooLong);
    }

    [Fact]
    public void SoftDelete_requires_reason_and_marks_deleted()
    {
        var now = T("2026-09-02T08:00Z");
        var p = Punch.Create(Tenant, Emp, T("2026-09-01T06:00Z"), new(2026, 9, 1), PunchDirection.In, PunchSource.Manual, null, null, null, null, null, true, User, "unuttu").Value;

        var blank = p.SoftDelete(User, " ", now);
        blank.IsFailure.ShouldBeTrue();
        blank.Error.Code.ShouldBe(AttendanceDomainErrors.PunchDeleteReasonRequired);
        p.IsDeleted.ShouldBeFalse();

        p.SoftDelete(User, "hatalı giriş", now).IsSuccess.ShouldBeTrue();
        p.DeletedAtUtc.ShouldBe(now);
        p.DeletedByUserId.ShouldBe(User);
        p.DeleteReason.ShouldBe("hatalı giriş");
        p.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public void SoftDelete_twice_is_rejected_and_keeps_first_audit()
    {
        var first = T("2026-09-02T08:00Z");
        var p = Punch.Create(Tenant, Emp, T("2026-09-01T06:00Z"), new(2026, 9, 1), PunchDirection.In, PunchSource.Manual, null, null, null, null, null, true, User, null).Value;
        p.SoftDelete(User, "hatalı giriş", first).IsSuccess.ShouldBeTrue();

        var again = p.SoftDelete(Guid.NewGuid(), "tekrar", first.AddHours(1));

        again.Error.Code.ShouldBe(AttendanceDomainErrors.PunchAlreadyDeleted);
        p.DeletedAtUtc.ShouldBe(first);
        p.DeletedByUserId.ShouldBe(User);
    }

    private static DateTime T(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
