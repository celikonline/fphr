using NSubstitute;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Absences;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// Ürün kararı (denetim A10): çalışanın takvimine göre iş günü olmayan güne (hafta sonu / resmî tatil) devamsızlık
/// kaydı eklenemez. Gün bilgisi Leave'in izin gün hesabındaki <see cref="ILeaveDayCalculator"/> üzerinden gelir.
/// </summary>
public sealed class AbsenceWorkingDayTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Employee = Guid.NewGuid();

    // 2026-10-12 is a Monday; Saturday 17th, Sunday 18th.
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DateOnly Saturday = new(2026, 10, 17);
    private static readonly DateOnly Sunday = new(2026, 10, 18);

    private readonly IAbsenceRecordRepository _absences = Substitute.For<IAbsenceRecordRepository>();

    private RecordAbsenceHandler CreateHandler(bool saturdayWorks = false, params DateOnly[] holidayDates)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Tenant);
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var leaveRequests = Substitute.For<ILeaveRequestRepository>();
        leaveRequests.ListActiveOverlappingAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var schedules = Substitute.For<IWorkScheduleReader>();
        schedules.GetWorkingDayPredicateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((Func<DateOnly, bool>)(d => d.DayOfWeek is not DayOfWeek.Sunday && (saturdayWorks || d.DayOfWeek is not DayOfWeek.Saturday)));
        schedules.GetIncludeSaturdayWhenFullWeekPredicateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((Func<DateOnly, bool>)(_ => false));

        var holidays = Substitute.For<IHolidayCalendarLookup>();
        var calendarId = Guid.NewGuid();
        holidays.ResolveCalendarAsync(Tenant, null, null, Arg.Any<CancellationToken>()).Returns(new HolidayCalendarRef(calendarId, "TR"));
        holidays.GetHolidaysAsync(Tenant, calendarId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<UpcomingHoliday>)holidayDates
                .Where(d => d >= call.ArgAt<DateOnly>(2) && d <= call.ArgAt<DateOnly>(3))
                .Select(d => new UpcomingHoliday(d, "Tatil"))
                .ToList());

        return new RecordAbsenceHandler(
            tenant, Substitute.For<ICurrentUser>(), dataScope, _absences, leaveRequests, new LeaveDayCalculator(schedules, holidays));
    }

    [Theory]
    [InlineData(AbsenceKind.Unexcused)]
    [InlineData(AbsenceKind.Excused)]
    public async Task Record_OnWeeklyRestDay_IsRejected(AbsenceKind kind)
    {
        var result = await CreateHandler().Handle(new RecordAbsenceCommand(Employee, Sunday, kind, null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveErrors.AbsenceNotOnWorkingDay);
        result.Error.Type.ShouldBe(ErrorType.Rule);
        _absences.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Record_LateOnSaturday_IsAlsoRejected()
    {
        var result = await CreateHandler().Handle(new RecordAbsenceCommand(Employee, Saturday, AbsenceKind.Late, 1m, null), CancellationToken.None);

        result.Error.Code.ShouldBe(LeaveErrors.AbsenceNotOnWorkingDay);
    }

    [Fact]
    public async Task Record_OnPublicHoliday_IsRejected()
    {
        var holiday = Monday.AddDays(1);

        var result = await CreateHandler(holidayDates: holiday).Handle(new RecordAbsenceCommand(Employee, holiday, AbsenceKind.Unexcused, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(LeaveErrors.AbsenceNotOnWorkingDay);
    }

    [Fact]
    public async Task Record_OnAWorkingDay_Succeeds()
    {
        var result = await CreateHandler().Handle(new RecordAbsenceCommand(Employee, Monday, AbsenceKind.Unexcused, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        _absences.ReceivedWithAnyArgs(1).Add(default!);
    }

    [Fact]
    public async Task Record_OnSaturday_Succeeds_WhenTheEmployeesScheduleWorksSaturdays()
    {
        var result = await CreateHandler(saturdayWorks: true).Handle(new RecordAbsenceCommand(Employee, Saturday, AbsenceKind.Unexcused, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
    }
}
