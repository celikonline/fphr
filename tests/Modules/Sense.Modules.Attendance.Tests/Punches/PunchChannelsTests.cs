using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.DeviceLogs;
using Sense.Modules.Attendance.Application.Import;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>
/// Z0.1: mevcut giriş kanalları (elle, PDKS Excel, PDKS cihazı, self-servis QR/GPS) önce ham okutma yazar, gün okutmalardan türetilir.
/// Gün sonuçları bugünkü davranışla aynı kalır (kanal testleri); burada okutma tarafı doğrulanır.
/// </summary>
public sealed class PunchChannelsTests
{
    private const string NationalId = "10000000146";
    private static readonly DateOnly Date = new(2026, 9, 16);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IShiftSettingsRepository _shiftSettings = Substitute.For<IShiftSettingsRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly InMemoryPunchRepository _punches = new();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly List<AttendanceDay> _stored = [];
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    // UTC+3 kiracı: 05:55Z = 08:55 yerel.
    private readonly FixedLocalClock _clock = new(new DateTime(2026, 9, 16, 15, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 16, 18, 0, 0));

    public PunchChannelsTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.UserId.Returns(_userId);
        _currentUser.EmployeeId.Returns(_employeeId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);
        _schedules.GetForDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, true));
        _days.When(d => d.Add(Arg.Any<AttendanceDay>())).Do(call => _stored.Add(call.Arg<AttendanceDay>()));
        _days.FindByEmployeeAndDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_stored.FirstOrDefault(d => d.Date == call.ArgAt<DateOnly>(1))));
    }

    private ImportDeviceLogsHandler DeviceHandler() =>
        new(_tenant, _employeeLookup, TestPlanner.From(_schedules), _days, _shiftSettings, _unitOfWork, _punches, _clock, _approvals);

    private RecordAttendanceHandler ManualHandler() => new(_days, TestPlanner.From(_schedules), _tenant, _dataScope, _clock, _punches, _currentUser, _approvals);

    private AttendanceImportRowHandler ExcelHandler() => new(_tenant, _employeeLookup, TestPlanner.From(_schedules), _days, _unitOfWork, _punches, _clock, _approvals);

    private AttendanceDay Day => _stored.Where(d => d.Date == Date).ShouldHaveSingleItem();

    private static string Csv(params string[] rows) => "employeeCode;timestamp;direction\n" + string.Join('\n', rows) + "\n";

    [Fact]
    public async Task Device_rows_become_punches_in_utc_and_the_day_is_derived_in_local_time()
    {
        var csv = Csv($"{NationalId};2026-09-16T08:55:00;in", $"{NationalId};2026-09-16T12:00:00;out", $"{NationalId};2026-09-16T12:45:00;in",
            $"{NationalId};2026-09-16T18:05:00;out");

        var result = await DeviceHandler().Handle(new ImportDeviceLogsCommand(csv), ct);

        result.Value.Imported.ShouldBe(4);
        _punches.Live.Select(p => (p.OccurredAtUtc.Hour, p.OccurredAtUtc.Minute, p.Direction)).ShouldBe(
            [(5, 55, PunchDirection.In), (9, 0, PunchDirection.Out), (9, 45, PunchDirection.In), (15, 5, PunchDirection.Out)]);
        _punches.Live.ShouldAllBe(p => p.Source == PunchSource.Device && p.WorkDate == Date && !p.IsManual);
        Day.CheckIn.ShouldBe(new TimeOnly(8, 55));
        Day.CheckOut.ShouldBe(new TimeOnly(18, 5));
        Day.ActualBreakMinutes.ShouldBe(45);
    }

    [Fact]
    public async Task Re_importing_the_same_device_file_adds_no_duplicate_punches()
    {
        var csv = Csv($"{NationalId};2026-09-16T08:55:00;in", $"{NationalId};2026-09-16T18:05:00;out");

        await DeviceHandler().Handle(new ImportDeviceLogsCommand(csv), ct);
        var second = await DeviceHandler().Handle(new ImportDeviceLogsCommand(csv), ct);

        second.Value.Errors.ShouldBeEmpty();
        _punches.All.Count.ShouldBe(2);
        Day.WorkedMinutes.ShouldBe(550);
    }

    [Fact]
    public async Task Device_double_in_is_incalculable_with_zero_minutes()
    {
        // Spec §5.1: yön sırası bozuk (In,In,Out) - bugüne kadar en erken giriş alınıp süre hesaplanıyordu.
        var csv = Csv($"{NationalId};2026-09-16T08:55:00;in", $"{NationalId};2026-09-16T09:10:00;in", $"{NationalId};2026-09-16T18:05:00;out");

        await DeviceHandler().Handle(new ImportDeviceLogsCommand(csv), ct);

        Day.Anomalies.ShouldBe(AttendanceAnomaly.Incalculable);
        Day.CheckIn.ShouldBe(new TimeOnly(8, 55));
        Day.WorkedMinutes.ShouldBe(0);
        _punches.Live.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Device_punch_on_a_day_recorded_before_punches_existed_keeps_the_old_values_as_punches()
    {
        var legacy = AttendanceDay.Record(_tenantId, _employeeId, Date, new TimeOnly(8, 55), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Device);
        _stored.Add(legacy);

        await DeviceHandler().Handle(new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T18:05:00;out")), ct);

        var seeded = _punches.Live.First();
        seeded.Direction.ShouldBe(PunchDirection.In);
        seeded.Reason.ShouldBe(PunchDayWriter.LegacyReason);
        seeded.OccurredAtUtc.ShouldBe(new DateTime(2026, 9, 16, 5, 55, 0, DateTimeKind.Utc));
        legacy.CheckIn.ShouldBe(new TimeOnly(8, 55));
        legacy.CheckOut.ShouldBe(new TimeOnly(18, 5));
    }

    [Fact]
    public async Task Manual_record_becomes_manual_punches_and_replaces_earlier_punches_of_the_day()
    {
        await DeviceHandler().Handle(new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T08:55:00;in", $"{NationalId};2026-09-16T18:05:00;out")), ct);

        var result = await ManualHandler().Handle(new RecordAttendanceCommand(_employeeId, Date, new TimeOnly(8, 30), new TimeOnly(18, 5)), ct);

        result.IsSuccess.ShouldBeTrue();
        _punches.Live.ShouldAllBe(p => p.Source == PunchSource.Manual && p.IsManual && p.CreatedByUserId == _userId);
        _punches.Live.Count.ShouldBe(2);
        var replaced = _punches.All.Where(p => p.IsDeleted).ToList();
        replaced.Count.ShouldBe(2);
        replaced.ShouldAllBe(p => p.DeleteReason == RecordAttendanceHandler.ReplacedReason && p.DeletedByUserId == _userId);
        Day.CheckIn.ShouldBe(new TimeOnly(8, 30));
        Day.Source.ShouldBe(AttendanceDaySource.Manual);
    }

    [Fact]
    public async Task Recording_the_same_manual_values_again_keeps_the_existing_punches()
    {
        var command = new RecordAttendanceCommand(_employeeId, Date, new TimeOnly(9, 0), new TimeOnly(18, 0));

        await ManualHandler().Handle(command, ct);
        await ManualHandler().Handle(command, ct);

        _punches.All.Count.ShouldBe(2);
        _punches.All.ShouldAllBe(p => !p.IsDeleted);
    }

    [Fact]
    public async Task Excel_import_row_becomes_import_punches()
    {
        var handler = ExcelHandler();
        var row = new ImportRow(2, new Dictionary<string, ImportCell>
        {
            [AttendanceImportColumns.NationalId] = new(NationalId),
            [AttendanceImportColumns.Date] = new("2026-09-16"),
            [AttendanceImportColumns.CheckIn] = new("09:00"),
            [AttendanceImportColumns.CheckOut] = new("18:00"),
        });

        await handler.ProcessAsync(new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], [row]), ct);

        _punches.Live.Select(p => (p.Direction, p.Source, p.IsManual)).ShouldBe([(PunchDirection.In, PunchSource.Import, false), (PunchDirection.Out, PunchSource.Import, false)]);
        Day.Source.ShouldBe(AttendanceDaySource.Import);
        Day.Anomalies.ShouldBe(AttendanceAnomaly.None);
    }

    [Fact]
    public async Task Self_service_punch_links_the_audit_row_and_a_second_in_is_a_no_op()
    {
        var locations = Substitute.For<ILocationLookup>();
        var selfCheckIns = Substitute.For<ISelfCheckInRepository>();
        var audits = new List<SelfCheckIn>();
        selfCheckIns.When(r => r.Add(Arg.Any<SelfCheckIn>())).Do(call => audits.Add(call.Arg<SelfCheckIn>()));
        var locationId = Guid.NewGuid();
        locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>()).Returns(new LocationGeofence(locationId, "HQ", null, null, null));
        QrCheckInHandler Handler(DateTime utc) =>
            new(_tenant, _currentUser, locations, _days, TestPlanner.From(_schedules), _shiftSettings, selfCheckIns, new FixedLocalClock(utc, utc.AddHours(3)), _punches, _approvals);

        await Handler(new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc)).Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), ct);
        await Handler(new DateTime(2026, 9, 16, 6, 5, 0, DateTimeKind.Utc)).Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), ct);

        audits.Count.ShouldBe(2);
        var punch = _punches.All.ShouldHaveSingleItem();
        punch.Source.ShouldBe(PunchSource.SelfService);
        punch.SourceRef.ShouldBe(audits[0].Id.ToString());
        punch.LocationId.ShouldBe(locationId);
        Day.CheckIn.ShouldBe(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task Second_self_service_out_supersedes_the_previous_out()
    {
        var locations = Substitute.For<ILocationLookup>();
        locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>()).Returns(new LocationGeofence(Guid.NewGuid(), "HQ", null, null, null));
        QrCheckInHandler Handler(DateTime utc) =>
            new(
                _tenant, _currentUser, locations, _days, TestPlanner.From(_schedules), _shiftSettings, Substitute.For<ISelfCheckInRepository>(), new FixedLocalClock(utc, utc.AddHours(3)),
                _punches, _approvals);

        await Handler(new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc)).Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), ct);
        await Handler(new DateTime(2026, 9, 16, 14, 0, 0, DateTimeKind.Utc)).Handle(new QrCheckInCommand("AB12CD34", PunchDirection.Out), ct);
        await Handler(new DateTime(2026, 9, 16, 15, 0, 0, DateTimeKind.Utc)).Handle(new QrCheckInCommand("AB12CD34", PunchDirection.Out), ct);

        _punches.Live.Count.ShouldBe(2);
        _punches.All.Single(p => p.IsDeleted).DeleteReason.ShouldNotBeNullOrWhiteSpace();
        Day.CheckOut.ShouldBe(new TimeOnly(18, 0));
        Day.Anomalies.ShouldBe(AttendanceAnomaly.None);
    }

    private void LockSeptember() =>
        _approvals.FindAsync(_employeeId, new DateOnly(2026, 9, 1), Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, Date, DateTime.UtcNow, null));

    private static ImportRow ExcelRow(int number, string date, string checkIn, string checkOut) =>
        new(number, new Dictionary<string, ImportCell>
        {
            [AttendanceImportColumns.NationalId] = new(NationalId),
            [AttendanceImportColumns.Date] = new(date),
            [AttendanceImportColumns.CheckIn] = new(checkIn),
            [AttendanceImportColumns.CheckOut] = new(checkOut),
        });

    [Fact]
    public async Task Manual_record_in_a_locked_month_is_rejected()
    {
        LockSeptember();

        var result = await ManualHandler().Handle(new RecordAttendanceCommand(_employeeId, Date, new TimeOnly(9, 0), new TimeOnly(18, 0)), ct);

        result.Error.Code.ShouldBe(AttendanceErrors.PeriodLocked);
        _punches.All.ShouldBeEmpty();
        _stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task Excel_row_in_a_locked_month_is_a_row_error_and_other_rows_still_apply()
    {
        LockSeptember();
        var rows = new[] { ExcelRow(2, "2026-09-16", "09:00", "18:00"), ExcelRow(3, "2026-08-31", "09:00", "18:00") };

        var result = await ExcelHandler().ProcessAsync(new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], rows), ct);

        result.Applied.ShouldBeTrue();
        var locked = result.Rows.Single(r => r.RowNumber == 2);
        locked.Outcome.ShouldBe(ImportRowOutcome.Error);
        locked.Errors.ShouldContain(e => e.Code == AttendanceErrors.PeriodLocked);
        result.Rows.Single(r => r.RowNumber == 3).Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _punches.Live.ShouldAllBe(p => p.WorkDate == new DateOnly(2026, 8, 31));
    }

    [Fact]
    public async Task Excel_locked_row_blocks_an_all_or_nothing_batch()
    {
        LockSeptember();
        var rows = new[] { ExcelRow(2, "2026-09-16", "09:00", "18:00"), ExcelRow(3, "2026-08-31", "09:00", "18:00") };

        var result = await ExcelHandler().ProcessAsync(new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.AllOrNothing, [AttendanceImportColumns.NationalId], rows), ct);

        result.Applied.ShouldBeFalse();
        _punches.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task Device_rows_in_a_locked_month_are_skipped_with_the_reason()
    {
        LockSeptember();
        var csv = Csv($"{NationalId};2026-09-16T08:55:00;in", $"{NationalId};2026-09-16T18:05:00;out", $"{NationalId};2026-08-31T09:00:00;in");

        var result = await DeviceHandler().Handle(new ImportDeviceLogsCommand(csv), ct);

        result.Value.Imported.ShouldBe(1);
        result.Value.Skipped.ShouldBe(2);
        result.Value.Errors.Where(e => e.Code == AttendanceErrors.PeriodLocked).Select(e => e.Line).ShouldBe([2, 3]);
        _punches.Live.ShouldHaveSingleItem().WorkDate.ShouldBe(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public async Task Self_service_check_in_in_a_locked_month_is_rejected()
    {
        LockSeptember();
        var locations = Substitute.For<ILocationLookup>();
        var selfCheckIns = Substitute.For<ISelfCheckInRepository>();
        locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>()).Returns(new LocationGeofence(Guid.NewGuid(), "HQ", null, null, null));
        var utc = new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc);
        var handler = new QrCheckInHandler(
            _tenant, _currentUser, locations, _days, TestPlanner.From(_schedules), _shiftSettings, selfCheckIns, new FixedLocalClock(utc, utc.AddHours(3)), _punches, _approvals);

        var result = await handler.Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), ct);

        result.Error.Code.ShouldBe(AttendanceErrors.PeriodLocked);
        _punches.All.ShouldBeEmpty();
        selfCheckIns.DidNotReceive().Add(Arg.Any<SelfCheckIn>());
    }

    [Fact]
    public async Task Manual_day_followed_by_device_punches_is_incalculable_and_explained_as_channel_conflict()
    {
        // İNSAN KARARI (I2): eskiden cihaz aktarımı elle girilmiş günün üzerine yazıyordu; artık iki kanalın okutmaları birlikte durur.
        await ManualHandler().Handle(new RecordAttendanceCommand(_employeeId, Date, new TimeOnly(9, 0), new TimeOnly(18, 0)), ct);

        await DeviceHandler().Handle(new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T08:55:00;in", $"{NationalId};2026-09-16T18:05:00;out")), ct);

        Day.Anomalies.ShouldBe(AttendanceAnomaly.Incalculable);
        _days.ListForEmployeeAsync(_employeeId, Date, Date, Arg.Any<CancellationToken>()).Returns(_stored.ToList());
        var listed = await new ListAttendanceDaysHandler(_days, _dataScope, _punches, _tenant, TestPlanner.From(_schedules)).Handle(new ListAttendanceDaysQuery(_employeeId, Date, Date), ct);
        listed.Value.ShouldHaveSingleItem().IncalculableReason.ShouldBe(IncalculableReason.ChannelConflict);
    }

    [Fact]
    public async Task Reimporting_real_break_on_a_backfilled_day_replaces_the_placeholder_break()
    {
        // Taşınmış gün: 08:55-18:05, PDKS molası 60 dk (gerçek ara saatler bilinmiyor → temsili çift). Aynı CSV'nin gerçek ara okutmalarıyla
        // yeniden içe alınması günü hesaplanamaz yapmamalı ve molayı ikiye katlamamalı.
        _stored.Add(AttendanceDay.Record(
            _tenantId, _employeeId, Date, new TimeOnly(8, 55), new TimeOnly(18, 5), new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Device,
            actualBreakMinutes: 60));
        var csv = Csv($"{NationalId};2026-09-16T08:55:00;in", $"{NationalId};2026-09-16T12:30:00;out", $"{NationalId};2026-09-16T13:30:00;in",
            $"{NationalId};2026-09-16T18:05:00;out");

        var result = await DeviceHandler().Handle(new ImportDeviceLogsCommand(csv), ct);

        result.Value.Errors.ShouldBeEmpty();
        Day.Anomalies.HasFlag(AttendanceAnomaly.Incalculable).ShouldBeFalse();
        Day.ActualBreakMinutes.ShouldBe(60);
        Day.CheckIn.ShouldBe(new TimeOnly(8, 55));
        Day.CheckOut.ShouldBe(new TimeOnly(18, 5));
        _punches.Live.Count(p => p.SourceRef?.StartsWith(LegacyDayPunches.BreakMarkerPrefix, StringComparison.Ordinal) == true).ShouldBe(2);
    }

    [Fact]
    public async Task Two_excel_rows_for_the_same_day_update_one_day_record()
    {
        var rows = new[] { ExcelRow(2, "2026-09-16", "09:00", "17:00"), ExcelRow(3, "2026-09-16", "09:00", "18:00") };

        await ExcelHandler().ProcessAsync(new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], rows), ct);

        Day.CheckOut.ShouldBe(new TimeOnly(18, 0));
        _punches.Live.Count.ShouldBe(2);
    }
}
