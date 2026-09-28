using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Shifts;

/// <summary>Gün türü Work olmayan satırlar (00:00 saatli) Assign/Repeat/Bulk limit denetimlerini tetiklememeli.</summary>
public sealed class DayOffLimitTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftDefinitionRepository _definitions = Substitute.For<IShiftDefinitionRepository>();
    private readonly IShiftSettingsRepository _settingsRepository = Substitute.For<IShiftSettingsRepository>();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();
    private readonly IEmployeeAvailabilityRepository _availability = Substitute.For<IEmployeeAvailabilityRepository>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly ShiftDefinition _day;
    private readonly List<ShiftAssignment> _stored = [];

    public DayOffLimitTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _employeeLookup.ExistsActiveAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _settingsRepository.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        _availability.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeAvailability>());
        _shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.Where(a => a.Date >= call.ArgAt<DateOnly>(0) && a.Date <= call.ArgAt<DateOnly>(1)).ToList());
        _shifts.FindByEmployeeAndDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.FirstOrDefault(a => a.Date == call.ArgAt<DateOnly>(1)));
        _day = ShiftDefinition.Create(_tenantId, new ShiftDefinitionValues(
            "GUNDUZ", "Gündüz", "#112233", ShiftType.Fixed, 9 * 60, 12 * 60, false, null, null, null,
            [], 0, 0, null, null, null, null, 0)).Value;
        _definitions.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<ShiftDefinition> { _day });
    }

    private void StoreWork(int dayOffset) =>
        _stored.Add(ShiftAssignment.Create(_tenantId, _employeeId, Monday.AddDays(dayOffset), new TimeOnly(9, 0), new TimeOnly(12, 0), 0, false, null).Value);

    private void StoreDayOff(int dayOffset, ShiftDayType type = ShiftDayType.WeeklyRest) =>
        _stored.Add(ShiftAssignment.CreateDayOff(_tenantId, _employeeId, Monday.AddDays(dayOffset), type).Value);

    private Task<Sense.Shared.Kernel.Results.Result<Guid>> Assign(int dayOffset) =>
        new AssignShiftHandler(_tenant, _shifts, _settingsRepository, _locks, _availability, _employeeLookup, _dataScope, _unitOfWork).Handle(
            new AssignShiftCommand(_employeeId, Monday.AddDays(dayOffset), new TimeOnly(9, 0), new TimeOnly(12, 0), 0, false, null), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Assign_next_to_day_off_row_does_not_trigger_rest_period_error()
    {
        StoreDayOff(0);

        var result = await Assign(1);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : "");
    }

    [Fact]
    public async Task Weekly_max_days_ignores_non_work_rows()
    {
        // 5 çalışma günü + 1 hafta tatili satırı: 7. gün 6. çalışma günüdür, sınır 6 -> geçmeli.
        for (var i = 0; i < 5; i++)
        {
            StoreWork(i);
        }

        StoreDayOff(5);

        var result = await Assign(6);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : "");
    }

    [Fact]
    public async Task Repeat_week_with_day_off_rows_counts_only_work_days()
    {
        for (var i = 0; i < 5; i++)
        {
            StoreWork(i);
        }

        StoreDayOff(5);
        StoreDayOff(6, ShiftDayType.RestDay);
        var nextWeek = Monday.AddDays(7);

        var result = await new RepeatShiftAssignmentsHandler(_tenant, _shifts, _settingsRepository, _locks, _availability, _dataScope, _unitOfWork).Handle(
            new RepeatShiftAssignmentsCommand(Monday, [nextWeek], [_employeeId]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : "");
        _shifts.Received(5).Add(Arg.Is<ShiftAssignment>(a => a.DayType == ShiftDayType.Work));
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.DayType == ShiftDayType.WeeklyRest && a.Date == nextWeek.AddDays(5)));
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.DayType == ShiftDayType.RestDay && a.Date == nextWeek.AddDays(6)));
    }

    [Fact]
    public async Task Bulk_assign_respects_weekly_limit_with_day_off_rows()
    {
        StoreDayOff(5);
        StoreDayOff(6, ShiftDayType.RestDay);

        var result = await BulkAssign(Enumerable.Range(0, 5).Select(i => Monday.AddDays(i)).ToArray());

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : "");
        result.Value.AffectedCount.ShouldBe(5);
    }

    [Fact]
    public async Task Bulk_assign_limit_violation_fails_whole_command()
    {
        // 7 gün x 3 saat, ama 7 çalışma günü haftalık gün sınırını (6) aşar.
        var result = await BulkAssign(Enumerable.Range(0, 7).Select(i => Monday.AddDays(i)).ToArray());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftWeeklyDaysExceeded);
    }

    private Task<Sense.Shared.Kernel.Results.Result<ShiftBulkResultDto>> BulkAssign(DateOnly[] dates) =>
        new BulkAssignShiftsHandler(_tenant, _shifts, _definitions, _settingsRepository, _locks, _availability, _employeeLookup, _dataScope, _unitOfWork).Handle(
            new BulkAssignShiftsCommand([_employeeId], dates, _day.Id, ShiftDayType.Work, false), TestContext.Current.CancellationToken);
}
