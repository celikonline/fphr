using Sense.Modules.Attendance.Domain.AttendanceDays;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class AttendanceDayDomainTests
{
    private static readonly TimeOnly PlannedStart = new(9, 0);
    private static readonly TimeOnly PlannedEnd = new(17, 0);
    private const int PlannedMinutes = 8 * 60;

    private static AttendanceDay RecordWorkingDay(TimeOnly? checkIn, TimeOnly? checkOut) =>
        AttendanceDay.Record(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), checkIn, checkOut, PlannedStart, PlannedEnd, PlannedMinutes, breakMinutes: 0, AttendanceDaySource.Manual);

    [Fact]
    public void Record_ExactPlannedHours_HasNoAnomalies()
    {
        var day = RecordWorkingDay(PlannedStart, PlannedEnd);

        day.WorkedMinutes.ShouldBe(PlannedMinutes);
        day.Anomalies.ShouldBe(AttendanceAnomaly.None);
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
    }

    [Fact]
    public void Record_LateCheckOut_FlagsLateLeaveAndOverwork()
    {
        // 09:00-19:00: planlanan çıkıştan geç (Geç çıkış) ve planlanandan fazla çalışılmış (Fazla çalışma).
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(19, 0));

        day.Anomalies.HasFlag(AttendanceAnomaly.LateLeave).ShouldBeTrue();
        day.Anomalies.HasFlag(AttendanceAnomaly.Overwork).ShouldBeTrue();
        day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeFalse();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);
    }

    [Fact]
    public void Record_EarlyCheckOut_FlagsEarlyLeaveAndUnderwork()
    {
        // 1 saat erken çıkış: yalnız Az çalışma bekleniyor (11.3.6: "erken çıkış çipi ayrı görülmedi" ama bayrak tutulur).
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(16, 0));

        day.Anomalies.HasFlag(AttendanceAnomaly.EarlyLeave).ShouldBeTrue();
        day.Anomalies.HasFlag(AttendanceAnomaly.Underwork).ShouldBeTrue();
        day.Anomalies.HasFlag(AttendanceAnomaly.Overwork).ShouldBeFalse();
    }

    [Fact]
    public void Record_LateArrival_FlagsLateArrival()
    {
        var day = RecordWorkingDay(new TimeOnly(9, 30), PlannedEnd);

        day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeTrue();
        day.WorkedMinutes.ShouldBe(PlannedMinutes - 30);
        day.Anomalies.HasFlag(AttendanceAnomaly.Underwork).ShouldBeTrue();
    }

    [Fact]
    public void Record_NoCheckInOrOut_OnWorkingDay_FlagsNoData()
    {
        var day = RecordWorkingDay(null, null);

        day.Anomalies.ShouldBe(AttendanceAnomaly.NoData);
        day.WorkedMinutes.ShouldBe(0);
    }

    [Fact]
    public void Record_OnlyCheckIn_FlagsMissingCheckOut()
    {
        var day = RecordWorkingDay(PlannedStart, null);

        day.Anomalies.ShouldBe(AttendanceAnomaly.MissingCheckOut);
    }

    [Fact]
    public void Record_DataOnNonWorkingDay_FlagsUnplannedWork()
    {
        var day = AttendanceDay.Record(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 20), new TimeOnly(9, 0), new TimeOnly(13, 0),
            plannedStart: null, plannedEnd: null, plannedMinutes: 0, breakMinutes: 0, AttendanceDaySource.Manual);

        day.Anomalies.ShouldBe(AttendanceAnomaly.UnplannedWork);
    }

    [Fact]
    public void Record_NoDataOnNonWorkingDay_HasNoAnomalies()
    {
        var day = AttendanceDay.Record(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 20), null, null,
            plannedStart: null, plannedEnd: null, plannedMinutes: 0, breakMinutes: 0, AttendanceDaySource.Manual);

        day.Anomalies.ShouldBe(AttendanceAnomaly.None);
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
    }

    [Fact]
    public void Record_SubtractsBreakMinutesFromWorked()
    {
        var day = AttendanceDay.Record(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), PlannedStart, PlannedEnd,
            PlannedStart, PlannedEnd, PlannedMinutes, breakMinutes: 60, AttendanceDaySource.Manual);

        day.WorkedMinutes.ShouldBe(PlannedMinutes - 60);
        day.Anomalies.HasFlag(AttendanceAnomaly.Underwork).ShouldBeTrue();
    }

    [Fact]
    public void UpdateRecord_RejectsCheckOutBeforeCheckIn()
    {
        var day = RecordWorkingDay(PlannedStart, PlannedEnd);

        var result = day.UpdateRecord(new TimeOnly(12, 0), new TimeOnly(10, 0), PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Manual);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Sense.Modules.Attendance.Domain.AttendanceDomainErrors.DayCheckOutBeforeCheckIn);
    }

    [Fact]
    public void UpdateRecord_ReimportOverwritesPreviousAnomalies()
    {
        var day = RecordWorkingDay(null, null);
        day.Anomalies.ShouldBe(AttendanceAnomaly.NoData);

        day.UpdateRecord(PlannedStart, PlannedEnd, PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Import);

        day.Anomalies.ShouldBe(AttendanceAnomaly.None);
        day.Source.ShouldBe(AttendanceDaySource.Import);
    }
}
