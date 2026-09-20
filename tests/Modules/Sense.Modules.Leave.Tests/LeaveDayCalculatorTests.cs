using NSubstitute;
using Sense.Modules.Leave.Application;
using Sense.Modules.Organization.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// Regression tests for LeaveDayCalculator's schedule window: like the real WorkScheduleReaderAdapter, the fake
/// reader only resolves dates inside the requested [from, to] window (anything else reads as "not a working day"),
/// so a calculator that asks for too narrow a window makes the Saturday rule fire for a single Friday.
/// </summary>
public sealed class LeaveDayCalculatorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Employee = Guid.NewGuid();

    private static LeaveDayCalculator CreateCalculator(bool saturdayFlag, params DateOnly[] holidayDates) =>
        CreateCalculator(saturdayFlag, halfDayDates: [], holidayDates);

    private static LeaveDayCalculator CreateCalculator(bool saturdayFlag, DateOnly[] halfDayDates, params DateOnly[] holidayDates)
    {
        var holidays = Substitute.For<IHolidayCalendarLookup>();
        var calendarId = Guid.NewGuid();
        holidays.ResolveCalendarAsync(Tenant, null, null, Arg.Any<CancellationToken>()).Returns(new HolidayCalendarRef(calendarId, "TR"));
        holidays.GetHolidaysAsync(Tenant, calendarId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var from = call.ArgAt<DateOnly>(2);
                var to = call.ArgAt<DateOnly>(3);
                return (IReadOnlyList<UpcomingHoliday>)holidayDates.Where(d => d >= from && d <= to).Select(d => new UpcomingHoliday(d, "Tatil", halfDayDates.Contains(d))).ToList();
            });

        return new LeaveDayCalculator(new WindowedScheduleReader(saturdayFlag), holidays);
    }

    private static Task<Sense.Modules.Leave.Domain.Calendar.LeaveDayCount> Count(LeaveDayCalculator calculator, DateOnly start, DateOnly end) =>
        calculator.CalculateAsync(Tenant, Employee, start, end, countsNonWorkingDays: false);

    [Fact]
    public async Task HalfDayHolidayFromCalendar_CountsHalfADay()
    {
        // 28 Ekim 2026 (Çarşamba) Cumhuriyet Bayramı Arifesi: yarım gün resmi tatil, yarım gün izin düşer.
        var arife = new DateOnly(2026, 10, 28);
        var result = await Count(CreateCalculator(saturdayFlag: false, halfDayDates: [arife], holidayDates: [arife]), arife, arife);
        result.Days.ShouldBe(0.5m);
    }

    [Fact]
    public async Task ArifeInsideARange_CountsHalfDayAndSkipsTheFullHoliday()
    {
        var arife = new DateOnly(2026, 10, 28);
        var bayram = new DateOnly(2026, 10, 29);
        var calculator = CreateCalculator(saturdayFlag: false, halfDayDates: [arife], holidayDates: [arife, bayram]);

        var result = await Count(calculator, new DateOnly(2026, 10, 26), bayram);

        result.Days.ShouldBe(2.5m);
    }

    [Fact]
    public async Task SingleFriday_CountsOneDay_EvenWhenSaturdayRuleIsOn()
    {
        var friday = new DateOnly(2026, 9, 25);
        var result = await Count(CreateCalculator(saturdayFlag: true), friday, friday);
        result.Days.ShouldBe(1m);
    }

    [Fact]
    public async Task ThursdayToFriday_WithSaturdayRuleOn_CountsThreeDays()
    {
        // 23–25 September 2026 is Wednesday–Friday: 3 working days, week not fully requested.
        var result = await Count(CreateCalculator(saturdayFlag: true), new DateOnly(2026, 9, 23), new DateOnly(2026, 9, 25));
        result.Days.ShouldBe(3m);
    }

    [Fact]
    public async Task SaturdayAlone_CountsZeroDays()
    {
        var saturday = new DateOnly(2026, 9, 26);
        var result = await Count(CreateCalculator(saturdayFlag: true), saturday, saturday);
        result.Days.ShouldBe(0m);
    }

    [Fact]
    public async Task FullMondayToFriday_WithSaturdayRuleOn_AddsTheSaturday()
    {
        var result = await Count(CreateCalculator(saturdayFlag: true), new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 25));
        result.Days.ShouldBe(6m);
    }

    [Fact]
    public async Task FullMondayToFriday_WithSaturdayRuleOff_CountsFiveDays()
    {
        var result = await Count(CreateCalculator(saturdayFlag: false), new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 25));
        result.Days.ShouldBe(5m);
    }

    [Fact]
    public async Task FridayToMonday_CountsTwoDays()
    {
        // Fri 25 Sep .. Mon 28 Sep: Saturday/Sunday are weekly rest and neither week is fully requested.
        var result = await Count(CreateCalculator(saturdayFlag: true), new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 28));
        result.Days.ShouldBe(2m);
    }

    [Fact]
    public async Task FullWeekContainingAPublicHoliday_StillGetsTheSaturdayAndSkipsTheHoliday()
    {
        // Mon 26 Oct .. Fri 30 Oct 2026 with Thu 29 Oct (Cumhuriyet Bayramı): 4 workdays; the holiday is a scheduled
        // working day inside the request, so the week is still fully requested and the Saturday is added.
        var result = await Count(CreateCalculator(saturdayFlag: true, new DateOnly(2026, 10, 29)), new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30));
        result.Days.ShouldBe(5m, "4 working days + the added Saturday; the public holiday is not counted");
        result.PublicHolidayDays.ShouldBe(1);
    }

    [Fact]
    public async Task HolidayOnFriday_SingleDayRequest_CountsZero()
    {
        var friday = new DateOnly(2026, 10, 30);
        var result = await Count(CreateCalculator(saturdayFlag: true, friday), friday, friday);
        result.Days.ShouldBe(0m);
    }

    /// <summary>Mimics WorkScheduleReaderAdapter: Mon–Fri schedule, only dates inside [from, to] are resolvable.</summary>
    private sealed class WindowedScheduleReader(bool saturdayFlag) : IWorkScheduleReader
    {
        public Task<Func<DateOnly, bool>> GetWorkingDayPredicateAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<Func<DateOnly, bool>>(d => d >= from && d <= to && d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

        public Task<IReadOnlyList<Sense.Modules.Leave.Domain.Overtime.ScheduledInterval>> GetScheduledIntervalsAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Sense.Modules.Leave.Domain.Overtime.ScheduledInterval>>([]);

        public Task<Func<DateOnly, bool>> GetIncludeSaturdayWhenFullWeekPredicateAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<Func<DateOnly, bool>>(d => saturdayFlag && d >= from && d <= to && d.DayOfWeek == DayOfWeek.Saturday);
    }
}
