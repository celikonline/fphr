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

public sealed class BulkShiftTests
{
    // 2026-09-28 is a Monday; 2026-10-01 falls in the next month.
    private static readonly DateOnly Monday = new(2026, 9, 28);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeB = Guid.NewGuid();
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
    private List<ShiftAssignment> _stored = [];

    public BulkShiftTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _dataScope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _employeeLookup.ExistsActiveAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _settingsRepository.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        _availability.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeAvailability>());
        _shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.Where(a => a.Date >= call.ArgAt<DateOnly>(0) && a.Date <= call.ArgAt<DateOnly>(1)
                && (call.ArgAt<IReadOnlyCollection<Guid>?>(2) is not { Count: > 0 } ids || ids.Contains(a.EmployeeId))).ToList());

        _day = ShiftDefinition.Create(_tenantId, new ShiftDefinitionValues(
            "GUNDUZ", "Gündüz", "#112233", ShiftType.Fixed, 9 * 60, 17 * 60 + 30, false, null, null, null,
            [ShiftBreak.Fixed(12 * 60, 30)], 0, 0, null, null, null, null, 0)).Value;
        _definitions.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<ShiftDefinition> { _day });
    }

    private BulkAssignShiftsHandler AssignHandler() =>
        new(_tenant, _shifts, _definitions, _settingsRepository, _locks, _availability, _employeeLookup, _dataScope, _unitOfWork);

    private ShiftAssignment Stored(Guid employeeId, DateOnly date, bool published = false)
    {
        var assignment = ShiftAssignment.Create(_tenantId, employeeId, date, new TimeOnly(8, 0), new TimeOnly(16, 0), 0, false, "eski").Value;
        if (published)
        {
            assignment.Publish(DateTime.UtcNow);
        }

        _stored.Add(assignment);
        return assignment;
    }

    private Task<Sense.Shared.Kernel.Results.Result<ShiftBulkResultDto>> Assign(
        Guid[] employees, DateOnly[] dates, Guid? definitionId, ShiftDayType dayType, bool overwrite = false) =>
        AssignHandler().Handle(new BulkAssignShiftsCommand(employees, dates, definitionId, dayType, overwrite), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Bulk_assign_skips_locked_period_and_reports_skipped()
    {
        // Hafta iki aya yayılıyor: eylül açık, ekim kilitli - kilit hücre bazında değerlendirilir.
        var october = new DateOnly(2026, 10, 1);
        _locks.FindByPeriodAsync(october, Arg.Any<CancellationToken>())
            .Returns(ShiftPlanLock.Lock(_tenantId, october, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual));

        var result = await Assign([_employeeA], [Monday.AddDays(2), Monday.AddDays(3)], _day.Id, ShiftDayType.Work);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AffectedCount.ShouldBe(1);
        result.Value.Skipped.ShouldBe([new ShiftBulkSkippedDto(_employeeA, october, ShiftBulkSkipReason.Locked)]);
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.Date == Monday.AddDays(2) && a.ShiftDefinitionId == _day.Id && a.DayType == ShiftDayType.Work));
    }

    [Fact]
    public async Task Bulk_assign_overwrites_draft_not_published_without_flag()
    {
        var draft = Stored(_employeeA, Monday);
        var published = Stored(_employeeA, Monday.AddDays(1), published: true);

        var result = await Assign([_employeeA], [Monday, Monday.AddDays(1)], _day.Id, ShiftDayType.Work);

        result.Value.AffectedCount.ShouldBe(1);
        result.Value.Skipped.ShouldBe([new ShiftBulkSkippedDto(_employeeA, Monday.AddDays(1), ShiftBulkSkipReason.Published)]);
        draft.ShiftDefinitionId.ShouldBe(_day.Id);
        draft.Start.ShouldBe(new TimeOnly(9, 0));
        draft.BreakMinutes.ShouldBe(30);
        published.Start.ShouldBe(new TimeOnly(8, 0));

        var forced = await Assign([_employeeA], [Monday.AddDays(1)], _day.Id, ShiftDayType.Work, overwrite: true);

        forced.Value.AffectedCount.ShouldBe(1);
        forced.Value.Skipped.ShouldBeEmpty();
        published.ShiftDefinitionId.ShouldBe(_day.Id);
    }

    [Fact]
    public async Task Work_day_requires_shift_definition()
    {
        var result = await Assign([_employeeA], [Monday], null, ShiftDayType.Work);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionRequired);
    }

    [Fact]
    public async Task Day_type_rest_day_stores_no_hours()
    {
        var existing = Stored(_employeeB, Monday);

        // dayType != work iken gönderilen vardiya tanımı yok sayılır.
        var result = await Assign([_employeeA, _employeeB], [Monday], _day.Id, ShiftDayType.RestDay);

        result.Value.AffectedCount.ShouldBe(2);
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a =>
            a.EmployeeId == _employeeA && a.DayType == ShiftDayType.RestDay && a.ShiftDefinitionId == null && a.ShiftMinutes == 0 && a.BreakMinutes == 0));
        existing.DayType.ShouldBe(ShiftDayType.RestDay);
        existing.ShiftDefinitionId.ShouldBeNull();
        existing.ShiftMinutes.ShouldBe(0);
        existing.Note.ShouldBeNull();
        await _definitions.DidNotReceive().ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bulk_clear_removes_drafts_and_skips_published()
    {
        var draft = Stored(_employeeA, Monday);
        Stored(_employeeA, Monday.AddDays(1), published: true);

        var result = await new BulkClearShiftsHandler(_tenant, _shifts, _locks, _employeeLookup, _dataScope).Handle(
            new BulkClearShiftsCommand([_employeeA], [Monday, Monday.AddDays(1), Monday.AddDays(2)], false), TestContext.Current.CancellationToken);

        result.Value.AffectedCount.ShouldBe(1);
        result.Value.Skipped.ShouldBe([new ShiftBulkSkippedDto(_employeeA, Monday.AddDays(1), ShiftBulkSkipReason.Published)]);
        _shifts.Received(1).Remove(draft);
    }

    [Fact]
    public async Task Unassigned_lists_employees_without_assignment_in_range()
    {
        Stored(_employeeA, Monday.AddDays(2));
        // Gün türü satırı da "atanmış" sayılır.
        var offDay = ShiftAssignment.CreateDayOff(_tenantId, _employeeB, Monday, ShiftDayType.WeeklyRest).Value;
        _stored.Add(offDay);
        var third = Guid.NewGuid();

        var result = await new ListUnassignedEmployeesHandler(_shifts, _dataScope).Handle(
            new ListUnassignedEmployeesQuery(Monday, Monday.AddDays(6), [_employeeA, _employeeB, third]), TestContext.Current.CancellationToken);

        result.Value.ShouldBe([third]);
    }

    [Fact]
    public async Task Totals_sum_shift_minutes_per_day_and_employee()
    {
        Stored(_employeeA, Monday); // 8 sa = 480
        Stored(_employeeB, Monday); // 480
        Stored(_employeeA, Monday.AddDays(1)); // 480
        _stored.Add(ShiftAssignment.CreateDayOff(_tenantId, _employeeB, Monday.AddDays(1), ShiftDayType.PublicHoliday).Value);

        var result = await new GetShiftPlanTotalsHandler(_shifts, _dataScope).Handle(
            new GetShiftPlanTotalsQuery(Monday, Monday.AddDays(6), null), TestContext.Current.CancellationToken);

        result.Value.ByDate.ShouldBe([new ShiftDayTotalDto(Monday, 2, 960), new ShiftDayTotalDto(Monday.AddDays(1), 1, 480)]);
        result.Value.ByEmployee.OrderBy(e => e.TotalMinutes).ToList()
            .ShouldBe(new[] { new ShiftEmployeeTotalDto(_employeeB, 480), new ShiftEmployeeTotalDto(_employeeA, 960) });
    }
}
