using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary><see cref="IPunchRecorder"/> (Z0.1): tek yazma yolu - idempotans, K6 kilidi, iş günü çözümü ve günün yeniden türetilmesi.</summary>
public sealed class PunchRecorderTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    // Kiracı UTC+3: 06:00Z = 09:00 yerel.
    private static readonly FixedLocalClock Clock = new(new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 20, 12, 0, 0));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly InMemoryPunchRepository _punches = new();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly IShiftSettingsRepository _shiftSettings = Substitute.For<IShiftSettingsRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly List<AttendanceDay> _addedDays = [];
    private readonly PunchRecorder recorder;
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public PunchRecorderTests()
    {
        _schedules.GetForDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), false));
        _days.When(d => d.Add(Arg.Any<AttendanceDay>())).Do(call => _addedDays.Add(call.Arg<AttendanceDay>()));
        _days.FindByEmployeeAndDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_addedDays.FirstOrDefault(d => d.Date == call.ArgAt<DateOnly>(1))));
        recorder = new PunchRecorder(_punches, _days, _schedules, _approvals, _shiftSettings, Clock, _unitOfWork);
    }

    private PunchInput Input(string utc, PunchDirectionDto direction, PunchSourceDto source = PunchSourceDto.Terminal, bool isManual = false) =>
        new(_tenantId, _employeeId, DateTime.SpecifyKind(DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc), direction,
            source, "ref-1", null, null, null, null, isManual, null, isManual ? "unutulan giriş" : null);

    private AttendanceDay DayRecord(DateOnly date) => _addedDays.Where(d => d.Date == date).ShouldHaveSingleItem();

    [Fact]
    public async Task Duplicate_punch_is_ignored_not_error()
    {
        var input = Input("2026-09-01T06:00", PunchDirectionDto.In);

        var r = await recorder.RecordBatchAsync(new[] { input, input }, ct);

        r.Value.ShouldBe(new PunchBatchResult(1, 1));
        _punches.All.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Punch_already_stored_is_ignored_by_a_later_batch()
    {
        var input = Input("2026-09-01T06:00", PunchDirectionDto.In);
        (await recorder.RecordAsync(input, ct)).IsSuccess.ShouldBeTrue();

        var r = await recorder.RecordBatchAsync([input, Input("2026-09-01T15:00", PunchDirectionDto.Out)], ct);

        r.Value.ShouldBe(new PunchBatchResult(1, 1));
        _punches.Live.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Recording_the_same_punch_twice_returns_the_first_id()
    {
        var input = Input("2026-09-01T06:00", PunchDirectionDto.In);

        var first = await recorder.RecordAsync(input, ct);
        var second = await recorder.RecordAsync(input, ct);

        second.Value.ShouldBe(first.Value);
        _punches.All.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Locked_month_rejects_manual_punch()
    {
        _approvals.FindAsync(_employeeId, new DateOnly(2026, 9, 1), Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, Day, DateTime.UtcNow, null));

        var r = await recorder.RecordAsync(Input("2026-09-01T06:00", PunchDirectionDto.In, PunchSourceDto.Manual, isManual: true), ct);

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe(AttendanceErrors.PeriodLocked);
        _punches.All.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Locked_month_punches_are_skipped_in_a_batch()
    {
        _approvals.FindAsync(_employeeId, new DateOnly(2026, 8, 1), Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, new DateOnly(2026, 8, 1), DateTime.UtcNow, null));

        var r = await recorder.RecordBatchAsync([Input("2026-08-31T06:00", PunchDirectionDto.In), Input("2026-09-01T06:00", PunchDirectionDto.In)], ct);

        r.Value.ShouldBe(new PunchBatchResult(1, 1));
        _punches.Live.ShouldHaveSingleItem().WorkDate.ShouldBe(Day);
    }

    [Fact]
    public async Task Recorded_pair_rebuilds_the_day_in_tenant_local_time()
    {
        await recorder.RecordAsync(Input("2026-09-01T06:00", PunchDirectionDto.In), ct);
        await recorder.RecordAsync(Input("2026-09-01T15:30", PunchDirectionDto.Out), ct);

        var day = DayRecord(Day);
        day.CheckIn.ShouldBe(new TimeOnly(9, 0));
        day.CheckOut.ShouldBe(new TimeOnly(18, 30));
        day.Source.ShouldBe(AttendanceDaySource.Terminal);
        day.WorkedMinutes.ShouldBe(510);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Night_shift_out_after_midnight_lands_on_the_shift_start_day()
    {
        _schedules.GetForDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(22, 0), new TimeOnly(6, 0), null, null, false));

        // 23:30 ve ertesi gün 05:50 yerel (UTC+3).
        await recorder.RecordAsync(Input("2026-09-01T20:30", PunchDirectionDto.In), ct);
        await recorder.RecordAsync(Input("2026-09-02T02:50", PunchDirectionDto.Out), ct);

        _punches.Live.ShouldAllBe(p => p.WorkDate == Day);
        var day = DayRecord(Day);
        day.CheckIn.ShouldBe(new TimeOnly(23, 30));
        day.CheckOut.ShouldBe(new TimeOnly(5, 50));
        day.Anomalies.HasFlag(AttendanceAnomaly.Incalculable).ShouldBeFalse();
        day.WorkedMinutes.ShouldBe(380);
        _addedDays.ShouldNotContain(d => d.Date == Day.AddDays(1));
    }

    [Fact]
    public async Task Broken_sequence_marks_the_day_incalculable_with_zero_minutes_and_no_other_flag()
    {
        await recorder.RecordBatchAsync(
            [
                Input("2026-09-01T06:00", PunchDirectionDto.In),
                Input("2026-09-01T09:00", PunchDirectionDto.Out),
                Input("2026-09-01T10:00", PunchDirectionDto.In),
            ],
            ct);

        var day = DayRecord(Day);
        day.Anomalies.ShouldBe(AttendanceAnomaly.Incalculable);
        day.WorkedMinutes.ShouldBe(0);
        day.NetWorkedMinutes.ShouldBe(0);
        day.OvertimeMinutes.ShouldBe(0);
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);
    }

    [Fact]
    public async Task Incalculable_day_gets_no_leave_overlap_flag()
    {
        await recorder.RecordAsync(Input("2026-09-01T06:00", PunchDirectionDto.Unknown), ct);

        DayRecord(Day).ApplyLeaveOverlap(overlapsApprovedLeave: true);

        DayRecord(Day).Anomalies.ShouldBe(AttendanceAnomaly.Incalculable);
    }

    [Fact]
    public async Task Single_punch_without_direction_is_incalculable()
    {
        await recorder.RecordAsync(Input("2026-09-01T06:00", PunchDirectionDto.Unknown), ct);

        DayRecord(Day).Anomalies.ShouldBe(AttendanceAnomaly.Incalculable);
    }

    [Fact]
    public async Task Invalid_punch_fails_the_whole_batch_without_saving()
    {
        var r = await recorder.RecordBatchAsync([Input("2026-09-01T06:00", PunchDirectionDto.In), Input("2099-01-01T06:00", PunchDirectionDto.Out)], ct);

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe(AttendanceDomainErrors.PunchInFuture);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Soft_delete_removes_the_punch_from_the_day()
    {
        await recorder.RecordAsync(Input("2026-09-01T06:00", PunchDirectionDto.In), ct);
        var wrong = await recorder.RecordAsync(Input("2026-09-01T07:00", PunchDirectionDto.In), ct);
        await recorder.RecordAsync(Input("2026-09-01T15:00", PunchDirectionDto.Out), ct);
        DayRecord(Day).Anomalies.ShouldBe(AttendanceAnomaly.Incalculable);

        var r = await recorder.SoftDeleteAsync(_tenantId, wrong.Value, Guid.NewGuid(), "çift okutma", ct);

        r.IsSuccess.ShouldBeTrue();
        _punches.All.Single(p => p.Id == wrong.Value).IsDeleted.ShouldBeTrue();
        var day = DayRecord(Day);
        day.Anomalies.ShouldBe(AttendanceAnomaly.None);
        day.CheckIn.ShouldBe(new TimeOnly(9, 0));
        day.CheckOut.ShouldBe(new TimeOnly(18, 0));
    }

    [Fact]
    public async Task Soft_delete_in_locked_month_is_rejected()
    {
        var punch = await recorder.RecordAsync(Input("2026-09-01T06:00", PunchDirectionDto.In), ct);
        _approvals.FindAsync(_employeeId, new DateOnly(2026, 9, 1), Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, Day, DateTime.UtcNow, null));

        var r = await recorder.SoftDeleteAsync(_tenantId, punch.Value, Guid.NewGuid(), "hatalı", ct);

        r.Error.Code.ShouldBe(AttendanceErrors.PeriodLocked);
        _punches.Live.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Soft_delete_of_unknown_punch_is_not_found()
    {
        var r = await recorder.SoftDeleteAsync(_tenantId, Guid.NewGuid(), Guid.NewGuid(), "hatalı", ct);

        r.Error.Code.ShouldBe(AttendanceErrors.PunchNotFound);
    }

    [Fact]
    public async Task Manual_punch_keeps_its_audit_fields()
    {
        var id = await recorder.RecordAsync(Input("2026-09-01T06:00", PunchDirectionDto.In, PunchSourceDto.Manual, isManual: true), ct);

        var punch = _punches.All.ShouldHaveSingleItem();
        punch.Id.ShouldBe(id.Value);
        punch.Source.ShouldBe(PunchSource.Manual);
        punch.IsManual.ShouldBeTrue();
        punch.Reason.ShouldBe("unutulan giriş");
        punch.SourceRef.ShouldBe("ref-1");
        punch.Direction.ShouldBe(PunchDirection.In);
    }

    [Fact]
    public async Task Same_punch_sent_100_ns_apart_is_ignored_as_duplicate()
    {
        // .NET 100 ns, PostgreSQL 1 µs: okutma anı mikrosaniyeye kırpılır, yoksa tekillik anahtarı ve idempotans ayrışır.
        var input = Input("2026-09-01T06:00:00.1234567", PunchDirectionDto.In);
        var retry = input with { OccurredAtUtc = input.OccurredAtUtc.AddTicks(1) };

        var batch = await recorder.RecordBatchAsync([input, retry], ct);
        var single = await recorder.RecordAsync(retry, ct);

        batch.Value.ShouldBe(new PunchBatchResult(1, 1));
        single.Value.ShouldBe(_punches.All.ShouldHaveSingleItem().Id);
        _punches.All[0].OccurredAtUtc.Ticks.ShouldBe(new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc).Ticks + 1_234_560);
    }

    [Fact]
    public async Task Source_ref_longer_than_limit_is_rejected_not_truncated()
    {
        var input = Input("2026-09-01T06:00", PunchDirectionDto.In) with { SourceRef = new string('x', Punch.SourceRefMaxLength + 1) };

        var r = await recorder.RecordAsync(input, ct);

        r.Error.Code.ShouldBe(AttendanceErrors.PunchSourceRefTooLong);
        _punches.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task Undefined_direction_or_source_is_rejected()
    {
        var badDirection = Input("2026-09-01T06:00", (PunchDirectionDto)99);
        var badSource = Input("2026-09-01T06:00", PunchDirectionDto.In, (PunchSourceDto)99);

        (await recorder.RecordAsync(badDirection, ct)).Error.Code.ShouldBe(AttendanceErrors.PunchDirectionInvalid);
        (await recorder.RecordBatchAsync([badSource], ct)).Error.Code.ShouldBe(AttendanceErrors.PunchSourceInvalid);
        _punches.All.ShouldBeEmpty();
    }
}
