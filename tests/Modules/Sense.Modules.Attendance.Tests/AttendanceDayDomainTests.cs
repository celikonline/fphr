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

    [Fact]
    public void ResolveWithOverride_UnderworkDay_SetsOverrideAndResolves()
    {
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(16, 0));

        var result = day.ResolveWithOverride(PlannedMinutes, "Elle düzeltildi");

        result.IsSuccess.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
        day.OverrideWorkedMinutes.ShouldBe(PlannedMinutes);
        day.OverrideReason.ShouldBe("Elle düzeltildi");
    }

    [Fact]
    public void ResolveWithOverride_NegativeMinutes_Fails()
    {
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(16, 0));

        var result = day.ResolveWithOverride(-1, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Sense.Modules.Attendance.Domain.AttendanceDomainErrors.OverrideMinutesInvalid);
    }

    [Fact]
    public void ResolveWithOverride_DayWithNoAnomalies_Fails()
    {
        var day = RecordWorkingDay(PlannedStart, PlannedEnd);

        var result = day.ResolveWithOverride(PlannedMinutes, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Sense.Modules.Attendance.Domain.AttendanceDomainErrors.NoAnomalyToResolve);
    }

    [Fact]
    public void ResolveWithOverride_AlreadyResolvedDay_Fails()
    {
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(16, 0));
        day.ResolveWithOverride(PlannedMinutes, null);

        var result = day.ResolveWithOverride(PlannedMinutes, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Sense.Modules.Attendance.Domain.AttendanceDomainErrors.DayAlreadyResolved);
    }

    [Fact]
    public void MarkResolvedByLinkedEntity_FlaggedDay_Resolves()
    {
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(19, 0));

        var result = day.MarkResolvedByLinkedEntity();

        result.IsSuccess.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
        day.OverrideWorkedMinutes.ShouldBeNull();
    }

    [Fact]
    public void Ignore_FlaggedDay_SetsIgnoredState()
    {
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(19, 0));

        var result = day.Ignore();

        result.IsSuccess.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Ignored);
    }

    [Fact]
    public void UpdateRecord_AfterResolution_ClearsOverride()
    {
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(16, 0));
        day.ResolveWithOverride(PlannedMinutes, "Elle düzeltildi");

        day.UpdateRecord(PlannedStart, PlannedEnd, PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Import);

        day.OverrideWorkedMinutes.ShouldBeNull();
        day.OverrideReason.ShouldBeNull();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
    }

    [Theory]
    [InlineData(AttendanceAnomaly.Overwork, AnomalyResolutionAction.AddOvertime, true)]
    [InlineData(AttendanceAnomaly.LateLeave, AnomalyResolutionAction.AddOvertime, true)]
    [InlineData(AttendanceAnomaly.Underwork, AnomalyResolutionAction.SetWorkedHours, true)]
    [InlineData(AttendanceAnomaly.Underwork, AnomalyResolutionAction.CreateLeaveRequest, true)]
    [InlineData(AttendanceAnomaly.Underwork, AnomalyResolutionAction.AddOvertime, false)]
    [InlineData(AttendanceAnomaly.Overwork, AnomalyResolutionAction.CreateLeaveRequest, false)]
    [InlineData(AttendanceAnomaly.NoData, AnomalyResolutionAction.SetWorkedHours, true)]
    [InlineData(AttendanceAnomaly.NoData, AnomalyResolutionAction.AddOvertime, false)]
    [InlineData(AttendanceAnomaly.LateArrival, AnomalyResolutionAction.Ignore, true)]
    [InlineData(AttendanceAnomaly.None, AnomalyResolutionAction.Ignore, false)]
    [InlineData(AttendanceAnomaly.WorkedWhileOnLeave, AnomalyResolutionAction.SetWorkedHours, true)]
    [InlineData(AttendanceAnomaly.WorkedWhileOnLeave, AnomalyResolutionAction.AddOvertime, false)]
    public void AnomalyResolutionAdvisor_AllowsExpectedActions(AttendanceAnomaly anomaly, AnomalyResolutionAction action, bool expected) =>
        AnomalyResolutionAdvisor.IsAllowed(anomaly, action).ShouldBe(expected);

    [Fact]
    public void ApplyLeaveOverlap_PunchedDayOverlappingApprovedLeave_FlagsWorkedWhileOnLeaveAndReopens()
    {
        var day = RecordWorkingDay(PlannedStart, PlannedEnd);
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved); // baseline: no anomaly yet

        day.ApplyLeaveOverlap(overlapsApprovedLeave: true);

        day.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);
    }

    [Fact]
    public void ApplyLeaveOverlap_NoPunch_DoesNotFlagEvenWhenOnApprovedLeave()
    {
        // İzin gününde hiç giriş/çıkış kaydı yoksa bu normal bir izin günüdür - anomali değil.
        var day = RecordWorkingDay(null, null);

        day.ApplyLeaveOverlap(overlapsApprovedLeave: true);

        day.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeFalse();
    }

    [Fact]
    public void ApplyLeaveOverlap_LeaveNoLongerApproved_ClearsFlagAndResolvesWhenNoOtherAnomaly()
    {
        var day = RecordWorkingDay(PlannedStart, PlannedEnd);
        day.ApplyLeaveOverlap(overlapsApprovedLeave: true);

        day.ApplyLeaveOverlap(overlapsApprovedLeave: false);

        day.Anomalies.ShouldBe(AttendanceAnomaly.None);
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
    }

    [Fact]
    public void ApplyLeaveOverlap_LeaveClearedButOtherAnomalyRemains_StaysUnresolved()
    {
        var day = RecordWorkingDay(PlannedStart, new TimeOnly(19, 0)); // Overwork/LateLeave zaten var
        day.ApplyLeaveOverlap(overlapsApprovedLeave: true);

        day.ApplyLeaveOverlap(overlapsApprovedLeave: false);

        day.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeFalse();
        day.Anomalies.HasFlag(AttendanceAnomaly.Overwork).ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);
    }

    [Fact]
    public void ApplyLeaveOverlap_CalledTwiceWithSameValue_IsIdempotent()
    {
        var day = RecordWorkingDay(PlannedStart, PlannedEnd);

        day.ApplyLeaveOverlap(overlapsApprovedLeave: true);
        day.ApplyLeaveOverlap(overlapsApprovedLeave: true);

        day.Anomalies.ShouldBe(AttendanceAnomaly.WorkedWhileOnLeave);
    }
}
