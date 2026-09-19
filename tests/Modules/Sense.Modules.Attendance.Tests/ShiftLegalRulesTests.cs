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

namespace Sense.Modules.Attendance.Tests;

/// <summary>QA regression: shift planning enforces the 11h daily rest, server-derived night shifts (m.69) and the configured minimum break.</summary>
public sealed class ShiftLegalRulesTests
{
    // 2026-09-21 is a Monday.
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftSettingsRepository _settingsRepository = Substitute.For<IShiftSettingsRepository>();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();
    private readonly IEmployeeAvailabilityRepository _availability = Substitute.For<IEmployeeAvailabilityRepository>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private ShiftSettings _settings;

    public ShiftLegalRulesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _settings = ShiftSettings.CreateDefault(_tenantId);
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _employeeLookup.ExistsActiveAsync(_tenantId, _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _settingsRepository.FindAsync(Arg.Any<CancellationToken>()).Returns(_ => _settings);
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        _shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment>());
        _availability.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeAvailability>());
    }

    private AssignShiftHandler CreateHandler() => new(_tenant, _shifts, _settingsRepository, _locks, _availability, _employeeLookup, _dataScope, _unitOfWork);

    private ShiftAssignment Existing(DateOnly date, int startHour, int endHour, int breakMinutes = 30, bool isNight = false) =>
        ShiftAssignment.Create(_tenantId, _employeeId, date, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), breakMinutes, isNight, null).Value;

    private void SeedWeek(params ShiftAssignment[] assignments) =>
        _shifts.ListForRangeAsync(Monday, Sunday, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(assignments.ToList());

    private Task<Sense.Shared.Kernel.Results.Result<Guid>> Assign(DateOnly date, int startHour, int endHour, int breakMinutes, bool isNight = false) =>
        CreateHandler().Handle(
            new AssignShiftCommand(_employeeId, date, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), breakMinutes, isNight, null), TestContext.Current.CancellationToken);

    // ---- 11h daily rest ----

    [Fact]
    public async Task Assign_LessThan11HoursAfterPreviousDayShift_IsRejected()
    {
        SeedWeek(Existing(Monday, 14, 22));

        var result = await Assign(Tuesday, 6, 14, 30);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftRestPeriodTooShort);
    }

    [Fact]
    public async Task Assign_Exactly11HoursAfterPreviousDayShift_IsAccepted()
    {
        SeedWeek(Existing(Monday, 14, 22));

        var result = await Assign(Tuesday, 9, 17, 30);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Assign_LessThan11HoursBeforeNextDayShift_IsRejected()
    {
        SeedWeek(Existing(Tuesday, 6, 14));

        var result = await Assign(Monday, 14, 22, 30);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftRestPeriodTooShort);
    }

    [Fact]
    public async Task Assign_OvernightPreviousShift_CountsRestFromItsEndOnTheNextMorning()
    {
        // Monday 22:00 -> Tuesday 06:00 (night, 7h30 net). Tuesday 14:00 leaves only 8h rest; Tuesday 17:00 leaves exactly 11h.
        var overnight = ShiftAssignment.Create(_tenantId, _employeeId, Monday, new TimeOnly(22, 0), new TimeOnly(6, 0), 30, true, null).Value;
        SeedWeek(overnight);

        var tooEarly = await Assign(Tuesday, 14, 20, 30);
        var enough = await Assign(Tuesday, 17, 23, 30);

        tooEarly.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftRestPeriodTooShort);
        enough.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Assign_MondayAfterSundayNightShiftOfPreviousWeek_ChecksRestAcrossTheWeekBoundary()
    {
        var previousSunday = Monday.AddDays(-1);
        var sundayNight = ShiftAssignment.Create(_tenantId, _employeeId, previousSunday, new TimeOnly(22, 0), new TimeOnly(6, 0), 30, true, null).Value;
        _shifts.ListForRangeAsync(previousSunday, previousSunday, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment> { sundayNight });

        var result = await Assign(Monday, 9, 17, 30);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftRestPeriodTooShort);
    }

    [Fact]
    public async Task Assign_SundayBeforeNextWeeksMondayShift_ChecksRestAcrossTheWeekBoundary()
    {
        var nextMonday = Monday.AddDays(7);
        _shifts.ListForRangeAsync(nextMonday, nextMonday, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment> { ShiftAssignment.Create(_tenantId, _employeeId, nextMonday, new TimeOnly(6, 0), new TimeOnly(14, 0), 30, false, null).Value });

        var result = await Assign(Sunday, 14, 22, 30);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftRestPeriodTooShort);
    }

    [Fact]
    public async Task Assign_ReplacingTheSameDaysShift_IsNotComparedWithItself()
    {
        var current = Existing(Monday, 9, 17);
        _shifts.FindByEmployeeAndDateAsync(_employeeId, Monday, Arg.Any<CancellationToken>()).Returns(current);
        SeedWeek(current, Existing(Tuesday, 9, 17));

        var result = await Assign(Monday, 9, 17, 30);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---- m.69 night work derived on the server ----

    [Fact]
    public async Task Assign_NightHoursWithoutClientFlag_AreTreatedAsNightShiftAndCapAt7h30()
    {
        // 22:00-06:00 is 8h gross; with no break and isNight=false the old code accepted it under the 11h day limit.
        var result = await Assign(Monday, 22, 6, 0, isNight: false);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDailyLimitExceeded);
    }

    [Fact]
    public async Task Assign_NightHoursWithinNightLimit_PersistsDerivedNightFlag()
    {
        var result = await Assign(Monday, 22, 6, 60, isNight: false);

        result.IsSuccess.ShouldBeTrue();
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.IsNight && a.ShiftMinutes == 420));
    }

    [Fact]
    public async Task Assign_DaytimeShift_IsNotMarkedNight()
    {
        var result = await Assign(Monday, 8, 16, 30, isNight: false);

        result.IsSuccess.ShouldBeTrue();
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => !a.IsNight));
    }

    [Fact]
    public async Task Assign_ClientNightFlag_IsStillHonoured()
    {
        // 08:00-17:00 with a 30 min break is 8h30 net: fine as a day shift, over the 7h30 night cap when the client marks it night.
        var asNight = await Assign(Monday, 8, 17, 30, isNight: true);
        var asDay = await Assign(Monday, 8, 17, 30, isNight: false);

        asNight.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDailyLimitExceeded);
        asDay.IsSuccess.ShouldBeTrue();
    }

    // ---- BreakMinMinutes ----

    [Fact]
    public async Task Assign_LongShiftWithoutBreak_IsRejected()
    {
        var result = await Assign(Monday, 9, 17, 0);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftBreakTooShort);
    }

    [Fact]
    public async Task Assign_BreakBelowConfiguredMinimum_IsRejected_AtOrAboveIsAccepted()
    {
        _settings.Update(660, 6, 2700, true, new TimeOnly(20, 0), new TimeOnly(6, 0), 450, breakMinMinutes: 45, false, false, 26, 3).IsSuccess.ShouldBeTrue();

        var below = await Assign(Monday, 9, 17, 30);
        var atMinimum = await Assign(Monday, 9, 17, 45);

        below.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftBreakTooShort);
        atMinimum.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Assign_ShiftShorterThanFourHours_NeedsNoBreak()
    {
        var result = await Assign(Monday, 9, 12, 0);

        result.IsSuccess.ShouldBeTrue();
    }
}

public sealed class ShiftSettingsNightShiftTests
{
    private static ShiftSettings Default() => ShiftSettings.CreateDefault(Guid.NewGuid());

    [Theory]
    [InlineData(22, 6, true)]
    [InlineData(20, 4, true)]
    [InlineData(18, 2, true)]   // 6h of 8h in the night window
    [InlineData(14, 22, false)] // 2h of 8h
    [InlineData(8, 16, false)]
    [InlineData(4, 12, false)]  // 2h of 8h
    [InlineData(6, 20, false)]  // no overlap
    public void IsNightShift_MoreThanHalfInsideTheWindow(int startHour, int endHour, bool expected) =>
        Default().IsNightShift(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)).ShouldBe(expected);

    [Fact]
    public void IsNightShift_NightWorkDisabled_IsAlwaysFalse()
    {
        var settings = Default();
        settings.Update(660, 6, 2700, false, null, null, null, 30, false, false, 26, 3).IsSuccess.ShouldBeTrue();

        settings.IsNightShift(new TimeOnly(22, 0), new TimeOnly(6, 0)).ShouldBeFalse();
    }
}
