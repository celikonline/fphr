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
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.ShiftDefinitions.ShiftDefinitionFixtures;

namespace Sense.Modules.Attendance.Tests.PunchPipeline;

/// <summary>
/// Zaman-devam I0 (B1 + B2): her gün kurma kanalı (elle, PDKS Excel, PDKS cihazı, self-servis, <see cref="IPunchRecorder"/>) planı K2
/// önceliğiyle <see cref="IPlannedDayResolver"/>'dan çözer - vardiya ataması + tanımı çalışma çizelgesinin önüne geçer ve tanımın kural seti
/// (tolerans, FM eşiği) güne uygulanır. Dinamik vardiya eşitliği ve plansız okutma günü Incalculable yapar.
/// </summary>
public sealed class PlanResolutionAcrossChannelsTests
{
    private const string NationalId = "10000000146";
    private static readonly DateOnly Date = new(2026, 9, 16);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IShiftAssignmentRepository _assignments = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftDefinitionRepository _definitions = Substitute.For<IShiftDefinitionRepository>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IShiftSettingsRepository _shiftSettings = Substitute.For<IShiftSettingsRepository>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly InMemoryPunchRepository _punches = new();
    private readonly List<AttendanceDay> _stored = [];
    private readonly PlannedDayResolver _planner;
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    // UTC+3 kiracı: 07:00Z = 10:00 yerel.
    private readonly FixedLocalClock _clock = new(new DateTime(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 16, 21, 0, 0));

    public PlanResolutionAcrossChannelsTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _tenant.IsResolved.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.EmployeeId.Returns(_employeeId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _employeeLookup.FindActiveByNationalIdAsync(_tenantId, NationalId, Arg.Any<CancellationToken>()).Returns(_employeeId);

        // Çalışma çizelgesi 09:00-18:00 - vardiya ataması varsa K2 gereği ondan önce gelmez.
        _schedules.GetForDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, true));
        _days.When(d => d.Add(Arg.Any<AttendanceDay>())).Do(call => _stored.Add(call.Arg<AttendanceDay>()));
        _days.FindByEmployeeAndDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_stored.FirstOrDefault(d => d.Date == call.ArgAt<DateOnly>(1))));
        _days.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<AttendanceDay>>(_stored.ToList()));
        _planner = new PlannedDayResolver(_schedules, _assignments, _definitions);
    }

    private AttendanceDay Day => _stored.Where(d => d.Date == Date).ShouldHaveSingleItem();

    private static string Csv(params string[] rows) => "employeeCode;timestamp;direction\n" + string.Join('\n', rows) + "\n";

    private ImportDeviceLogsHandler DeviceHandler() =>
        new(_tenant, _employeeLookup, _planner, _days, _shiftSettings, _unitOfWork, _punches, _clock, _approvals);

    private void AssignDefinition(ShiftDefinition assigned, params ShiftDefinition[] others)
    {
        foreach (var definition in others.Prepend(assigned))
        {
            _definitions.GetByIdAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(definition);
        }

        _definitions.ListAsync(false, Arg.Any<CancellationToken>()).Returns(others.Prepend(assigned).Where(d => d.IsActive).ToList());
        var assignment = ShiftAssignment.Create(_tenantId, _employeeId, Date, new TimeOnly(9, 0), new TimeOnly(18, 0), 0, false, null).Value;
        assignment.SetPlan(assigned.Id, ShiftDayType.Work);
        _assignments.FindByEmployeeAndDateAsync(_employeeId, Date, Arg.Any<CancellationToken>()).Returns(assignment);
    }

    [Fact]
    public async Task Device_import_applies_the_shift_definition_tolerance_and_overtime_threshold()
    {
        AssignDefinition(ShiftDefinition.Create(
            _tenantId, FixedValues("SAB", Minute(9), Minute(18)) with { LateToleranceMinutes = 10, LateDepartureOvertimeMinMinutes = 30 }).Value);

        await DeviceHandler().Handle(
            new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T09:08:00;in", $"{NationalId};2026-09-16T18:20:00;out")), ct);

        Day.PlannedStart.ShouldBe(new TimeOnly(9, 0));
        Day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeFalse("09:08 giriş 10 dk tolerans içinde");
        Day.OvertimeMinutes.ShouldBe(0, "18:20 çıkışın 20 dk'sı 30 dk FM eşiğinin altında");
    }

    [Fact]
    public async Task Device_import_without_a_definition_keeps_the_exact_schedule_comparison()
    {
        await DeviceHandler().Handle(
            new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T09:08:00;in", $"{NationalId};2026-09-16T18:20:00;out")), ct);

        Day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeTrue();
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("excel")]
    [InlineData("device")]
    [InlineData("selfService")]
    [InlineData("recorder")]
    public async Task Every_channel_resolves_the_same_K2_plan(string channel)
    {
        AssignDefinition(Fixed("GEC", Minute(10), Minute(19)));

        await RecordThroughAsync(channel);

        Day.PlannedStart.ShouldBe(new TimeOnly(10, 0), $"{channel}: vardiya ataması çizelgenin (09:00) önüne geçer");
        Day.PlannedEnd.ShouldBe(new TimeOnly(19, 0));
        Day.CheckIn.ShouldBe(new TimeOnly(10, 0));
        Day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeFalse();
    }

    [Fact]
    public async Task Dynamic_shift_tie_makes_the_day_incalculable_and_is_explained()
    {
        var early = Dynamic("D06", Minute(6), Minute(14));
        var late = Dynamic("D10", Minute(10), Minute(18));
        AssignDefinition(early, late);

        await DeviceHandler().Handle(
            new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T08:00:00;in", $"{NationalId};2026-09-16T16:00:00;out")), ct);

        Day.Anomalies.ShouldBe(AttendanceAnomaly.Incalculable, "08:00 iki dinamik vardiyanın başlangıcına eşit uzaklıkta");
        Day.WorkedMinutes.ShouldBe(0);
        var listed = await new ListAttendanceDaysHandler(_days, _dataScope, _punches, _tenant, _planner)
            .Handle(new ListAttendanceDaysQuery(_employeeId, Date, Date), ct);
        listed.Value.ShouldHaveSingleItem().IncalculableReason.ShouldBe(IncalculableReason.AmbiguousShift);
    }

    [Fact]
    public async Task Punches_without_any_plan_make_the_day_incalculable_and_are_explained()
    {
        _schedules.GetForDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(false, null, null, null, null, false, HasSchedule: false));

        await DeviceHandler().Handle(
            new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T09:00:00;in", $"{NationalId};2026-09-16T18:00:00;out")), ct);

        Day.Anomalies.ShouldBe(AttendanceAnomaly.Incalculable, "ne vardiya ataması ne çalışma çizelgesi var");
        var listed = await new ListAttendanceDaysHandler(_days, _dataScope, _punches, _tenant, _planner)
            .Handle(new ListAttendanceDaysQuery(_employeeId, Date, Date), ct);
        listed.Value.ShouldHaveSingleItem().IncalculableReason.ShouldBe(IncalculableReason.NoPlan);
    }

    [Fact]
    public async Task Schedule_off_day_is_a_plan_so_punches_stay_unplanned_work_not_incalculable()
    {
        _schedules.GetForDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(false, null, null, null, null, false));

        await DeviceHandler().Handle(
            new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T09:00:00;in", $"{NationalId};2026-09-16T18:00:00;out")), ct);

        Day.Anomalies.ShouldBe(AttendanceAnomaly.UnplannedWork);
    }

    private async Task RecordThroughAsync(string channel)
    {
        switch (channel)
        {
            case "manual":
                (await new RecordAttendanceHandler(_days, _planner, _tenant, _dataScope, _clock, _punches, _currentUser, _approvals, Substitute.For<IAttendanceUnitOfWork>())
                    .Handle(new RecordAttendanceCommand(_employeeId, Date, new TimeOnly(10, 0), new TimeOnly(19, 0)), ct)).IsSuccess.ShouldBeTrue();
                break;
            case "excel":
                var row = new ImportRow(2, new Dictionary<string, ImportCell>
                {
                    [AttendanceImportColumns.NationalId] = new(NationalId),
                    [AttendanceImportColumns.Date] = new("2026-09-16"),
                    [AttendanceImportColumns.CheckIn] = new("10:00"),
                    [AttendanceImportColumns.CheckOut] = new("19:00"),
                });
                await new AttendanceImportRowHandler(_tenant, _employeeLookup, _planner, _days, _unitOfWork, _punches, _clock, _approvals)
                    .ProcessAsync(new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AttendanceImportColumns.NationalId], [row]), ct);
                break;
            case "device":
                await DeviceHandler().Handle(
                    new ImportDeviceLogsCommand(Csv($"{NationalId};2026-09-16T10:00:00;in", $"{NationalId};2026-09-16T19:00:00;out")), ct);
                break;
            case "selfService":
                var locations = Substitute.For<ILocationLookup>();
                locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>())
                    .Returns(new LocationGeofence(Guid.NewGuid(), "HQ", null, null, null));
                var at = new DateTime(2026, 9, 16, 7, 0, 0, DateTimeKind.Utc);
                (await new QrCheckInHandler(
                        _tenant, _currentUser, locations, _days, _planner, _shiftSettings, Substitute.For<ISelfCheckInRepository>(),
                        new FixedLocalClock(at, at.AddHours(3)), _punches, _approvals, Substitute.For<IAttendanceUnitOfWork>())
                    .Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), ct)).IsSuccess.ShouldBeTrue();
                break;
            case "recorder":
                var recorder = new PunchRecorder(
                    _punches, _days, _planner, _approvals, _shiftSettings, _clock, _unitOfWork, _tenant, Substitute.For<ITenantContextSetter>());
                (await recorder.RecordAsync(
                    new PunchInput(_tenantId, _employeeId, new DateTime(2026, 9, 16, 7, 0, 0, DateTimeKind.Utc), PunchDirectionDto.In, PunchSourceDto.Terminal,
                        null, null, null, null, null, false, null, null), ct)).IsSuccess.ShouldBeTrue();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
        }
    }
}
