using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.DeviceLogs;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class ImportDeviceLogsHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IShiftSettingsRepository _shiftSettings = Substitute.For<IShiftSettingsRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();

    private const string EmployeeCode = "10000000146";
    private static readonly DateOnly Date = new(2026, 9, 16);

    public ImportDeviceLogsHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _schedules.GetForDateAsync(_employeeId, Date, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, true));
    }

    private ImportDeviceLogsHandler CreateHandler() =>
        new(_tenant, _employeeLookup, TestPlanner.From(_schedules), _days, _shiftSettings, _unitOfWork, new InMemoryPunchRepository(), new FixedLocalClock(DateTime.UtcNow),
            Substitute.For<IMonthlyTimesheetApprovalRepository>());

    [Fact]
    public async Task Handle_InAndOutPunchesSameDay_UpsertsSingleAttendanceDay()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T18:05:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(2);
        result.Value.Skipped.ShouldBe(0);
        result.Value.Errors.ShouldBeEmpty();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.CheckIn == new TimeOnly(8, 55) && d.CheckOut == new TimeOnly(18, 5)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MultipleInPunches_UsesEarliestAsCheckIn()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T09:10:00;in\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T18:05:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.CheckIn == new TimeOnly(8, 55)));
    }

    [Fact]
    public async Task Handle_UnknownEmployeeCode_SkipsRowWithError()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(0);
        result.Value.Skipped.ShouldBe(1);
        result.Value.Errors.ShouldContain(e => e.Line == 2 && e.Code == DeviceLogErrorCodes.UnknownEmployee);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task Handle_DuplicateEmployeeAndTimestamp_SkipsSecondOccurrence()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(1);
        result.Value.Skipped.ShouldBe(1);
        result.Value.Errors.ShouldContain(e => e.Line == 3 && e.Code == DeviceLogErrorCodes.DuplicateTimestamp);
    }

    [Fact]
    public async Task Handle_DerivedCheckOutBeforeCheckIn_SkipsGroupWithError()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        // "out" at 07:00 is before the single "in" at 09:00 on the same day - malformed device data.
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T09:00:00;in\n" +
                            "10000000146;2026-09-16T07:00:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(0);
        result.Value.Skipped.ShouldBe(2);
        result.Value.Errors.ShouldAllBe(e => e.Code == DeviceLogErrorCodes.CheckOutBeforeCheckIn);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingAttendanceDay_UpdatesInsteadOfAdding()
    {
        var existing = AttendanceDay.Record(_tenantId, _employeeId, Date, null, null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Manual);
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns(existing);
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.CheckIn.ShouldBe(new TimeOnly(8, 55));
        existing.Source.ShouldBe(AttendanceDaySource.Device);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task Handle_ParserLevelError_IsIncludedInResultErrors()
    {
        const string csv = "employeeCode;timestamp;direction\n;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Skipped.ShouldBe(1);
        result.Value.Errors.ShouldContain(e => e.Code == DeviceLogErrorCodes.MissingEmployeeCode);
    }

    // G-6-14 (4857 md.69): a night shift's "out" punch (past midnight, on the next calendar day) is attributed
    // back to the shift's own start day instead of forming a broken pair on either side.
    [Fact]
    public async Task Handle_NightShiftInBeforeMidnightOutAfter_AssignsBothPunchesToStartDay()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T23:50:00;in\n" +
                            "10000000146;2026-09-17T06:10:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(2);
        result.Value.Skipped.ShouldBe(0);
        result.Value.Errors.ShouldBeEmpty();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d =>
            d.Date == Date && d.CheckIn == new TimeOnly(23, 50) && d.CheckOut == new TimeOnly(6, 10) && d.WorkedMinutes == 380));
        // Only the start-day's AttendanceDay is touched - the next calendar day is never looked up as its own record.
        await _days.DidNotReceive().FindByEmployeeAndDateAsync(_employeeId, Date.AddDays(1), Arg.Any<CancellationToken>());
    }

    // Regular day shift (checkout same day, well before midnight) must stay exactly as today's behavior.
    [Fact]
    public async Task Handle_RegularDayShift_IsNotTreatedAsNightShift()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T18:05:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.CheckIn == new TimeOnly(8, 55) && d.CheckOut == new TimeOnly(18, 5) && d.WorkedMinutes == 550));
    }

    // A stray morning "out" with no matching previous-day evening "in" (in this file or an existing record) keeps
    // today's behavior: its own day, missing check-in anomaly - not silently reassigned.
    [Fact]
    public async Task Handle_MorningOutWithoutPreviousDayCheckIn_StaysOnOwnDay()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date.AddDays(-1), Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T06:10:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(1);
        result.Value.Skipped.ShouldBe(0);
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.Date == Date && d.CheckIn == null && d.CheckOut == new TimeOnly(6, 10)));
    }

    // G-6-22/23 (data-loss regression, PO analysis 18:52): PDKS exports commonly arrive as separate partial
    // batches - the previous evening's "in" punch landed in an *earlier* import and is already on the existing
    // AttendanceDay record, not in this batch's own "ins" list. The handler must merge it in instead of
    // overwriting CheckIn with null when it recomputes the night-shift record.
    [Fact]
    public async Task Handle_CrossBatchNightShiftOutPunch_PreservesCheckInFromEarlierImport()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        var existingFromEarlierBatch = AttendanceDay.Record(
            _tenantId, _employeeId, Date, new TimeOnly(23, 50), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Device);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns(existingFromEarlierBatch);
        // This second batch only carries the next calendar day's "out" - the "in" is not in this file at all.
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-17T06:10:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(1);
        result.Value.Skipped.ShouldBe(0);
        existingFromEarlierBatch.CheckIn.ShouldBe(new TimeOnly(23, 50));
        existingFromEarlierBatch.CheckOut.ShouldBe(new TimeOnly(6, 10));
        existingFromEarlierBatch.WorkedMinutes.ShouldBe(380);
        existingFromEarlierBatch.Anomalies.HasFlag(AttendanceAnomaly.MissingCheckIn).ShouldBeFalse();
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    // Symmetric case: this batch only carries the "in" - the day's already-recorded CheckOut must survive.
    [Fact]
    public async Task Handle_BatchWithOnlyCheckIn_PreservesExistingCheckOut()
    {
        var existing = AttendanceDay.Record(
            _tenantId, _employeeId, Date, null, new TimeOnly(18, 5), new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Device);
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns(existing);
        const string csv = "employeeCode;timestamp;direction\n10000000146;2026-09-16T08:55:00;in\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.CheckIn.ShouldBe(new TimeOnly(8, 55));
        existing.CheckOut.ShouldBe(new TimeOnly(18, 5));
        existing.WorkedMinutes.ShouldBe(550);
    }

    // G-6-24 (idempotency, PO analysis 18:52): re-importing the exact same "out" row a second time must not create
    // a second day record or a second update - the existing DuplicateTimestamp/seenTimestamps guard already covers
    // this within a single Handle call; this locks that behavior in for the device-log path specifically.
    [Fact]
    public async Task Handle_SameOutRowImportedTwiceInOneBatch_SkipsSecondOccurrenceAndUpdatesOnce()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T18:05:00;out\n" +
                            "10000000146;2026-09-16T18:05:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Imported.ShouldBe(1);
        result.Value.Skipped.ShouldBe(1);
        result.Value.Errors.ShouldContain(e => e.Line == 3 && e.Code == DeviceLogErrorCodes.DuplicateTimestamp);
        _days.Received(1).Add(Arg.Any<AttendanceDay>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // Kart senseik-014 §1: an intermediate Out→In pair between the first "in" and the last "out" (öğle çıkışı) is a
    // real observed break - it should feed AttendanceDay.ActualBreakMinutes, not be lost as just "another punch".
    [Fact]
    public async Task Handle_LunchOutInBetweenFirstInAndLastOut_RecordsActualBreakMinutes()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:55:00;in\n" +
                            "10000000146;2026-09-16T12:00:00;out\n" +
                            "10000000146;2026-09-16T12:45:00;in\n" +
                            "10000000146;2026-09-16T18:05:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.CheckIn == new TimeOnly(8, 55) && d.CheckOut == new TimeOnly(18, 5) && d.ActualBreakMinutes == 45));
    }

    // Same scenario but the observed lunch break (20 dk) is shorter than the m.68 legal minimum for a >7.5s gross
    // span (1 saat) - BreakMinutesApplied must use the legal minimum, not the shorter observed value.
    [Fact]
    public async Task Handle_ObservedBreakShorterThanLegalMinimum_AppliesLegalMinimum()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T08:00:00;in\n" +
                            "10000000146;2026-09-16T12:00:00;out\n" +
                            "10000000146;2026-09-16T12:20:00;in\n" +
                            "10000000146;2026-09-16T18:00:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.ActualBreakMinutes == 20 && d.BreakMinutesApplied == 60 && d.NetWorkedMinutes == 540));
    }

    // Kart senseik-014 §3: tenant ShiftSettings enforces m.63 (11s) and m.69 (7,5s gece) once resolved.
    [Fact]
    public async Task Handle_DayExceedsDailyAndNightLimits_FlagsBothOnAttendanceDay()
    {
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, EmployeeCode, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _days.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        _shiftSettings.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        // 18:00 → next day 08:00 (overnight): 14 saat brüt (m.68 asgari mola 1sa → 13s net, m.63'ü aşar), gece
        // penceresiyle (20:00-06:00) 10 saat kesişir (m.69'un 7,5 saatini aşar).
        const string csv = "employeeCode;timestamp;direction\n" +
                            "10000000146;2026-09-16T18:00:00;in\n" +
                            "10000000146;2026-09-17T08:00:00;out\n";

        var result = await CreateHandler().Handle(new ImportDeviceLogsCommand(csv), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.DailyLimitExceeded && d.NightLimitExceeded && d.NightMinutes == 600 && d.NetWorkedMinutes == 780));
    }
}
