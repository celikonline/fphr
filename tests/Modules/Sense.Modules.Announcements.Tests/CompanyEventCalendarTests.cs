using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.CompanyEvents;
using Sense.Modules.Announcements.Domain;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>
/// Pure "hangi doğum günü hangi tarihte görünür" logic (senseik-027 acceptance: "domain birim testleri - etkinlik
/// türetme mantığı - doğum günü/izin başlangıcı"). Leave-start derivation itself has no interesting math (the port
/// already scopes to [from, to]) - it is covered end to end in CompanyEventQueriesTests.
/// </summary>
public sealed class CompanyEventCalendarTests
{
    [Fact]
    public void ValidateRange_WhenToBeforeFrom_Fails()
    {
        var result = CompanyEventCalendar.ValidateRange(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementsErrors.CalendarInvalidRange, result.Error.Code);
    }

    [Fact]
    public void ValidateRange_WhenRangeExceedsMax_Fails()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(CompanyEventLimits.CalendarMaxRangeDays); // one day too many

        var result = CompanyEventCalendar.ValidateRange(from, to);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementsErrors.CalendarRangeTooLarge, result.Error.Code);
    }

    [Fact]
    public void ValidateRange_WhenWithinMax_Succeeds()
    {
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);

        var result = CompanyEventCalendar.ValidateRange(from, to);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void DeriveBirthdays_WhenBirthdayFallsInsideRange_ReturnsEntryThisYear()
    {
        var employeeId = Guid.NewGuid();
        var birthDates = new Dictionary<Guid, DateOnly> { [employeeId] = new(1990, 9, 15) };

        var result = CompanyEventCalendar.DeriveBirthdays(birthDates, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var entry = Assert.Single(result);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(new DateOnly(2026, 9, 15), entry.Date);
    }

    [Fact]
    public void DeriveBirthdays_WhenBirthdayOutsideRange_ReturnsNothing()
    {
        var birthDates = new Dictionary<Guid, DateOnly> { [Guid.NewGuid()] = new(1990, 3, 15) };

        var result = CompanyEventCalendar.DeriveBirthdays(birthDates, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Empty(result);
    }

    [Fact]
    public void DeriveBirthdays_WhenRangeSpansYearBoundary_MatchesBirthdayInEitherYear()
    {
        var employeeId = Guid.NewGuid();
        var birthDates = new Dictionary<Guid, DateOnly> { [employeeId] = new(1985, 1, 2) };

        // Month-view padding around New Year's: 30 Nov 2026 - 3 Jan 2027.
        var result = CompanyEventCalendar.DeriveBirthdays(birthDates, new DateOnly(2026, 11, 30), new DateOnly(2027, 1, 3));

        var entry = Assert.Single(result);
        Assert.Equal(new DateOnly(2027, 1, 2), entry.Date);
    }

    [Fact]
    public void DeriveBirthdays_WhenBornOnLeapDay_CelebratedOn28FebInNonLeapYear()
    {
        var employeeId = Guid.NewGuid();
        var birthDates = new Dictionary<Guid, DateOnly> { [employeeId] = new(1992, 2, 29) };

        // 2026 is not a leap year.
        var result = CompanyEventCalendar.DeriveBirthdays(birthDates, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));

        var entry = Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 2, 28), entry.Date);
    }

    [Fact]
    public void DeriveBirthdays_WithMultipleEmployees_ReturnsOnlyThoseInRange()
    {
        var inRange = Guid.NewGuid();
        var outOfRange = Guid.NewGuid();
        var birthDates = new Dictionary<Guid, DateOnly>
        {
            [inRange] = new(1988, 9, 5),
            [outOfRange] = new(1988, 12, 25),
        };

        var result = CompanyEventCalendar.DeriveBirthdays(birthDates, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var entry = Assert.Single(result);
        Assert.Equal(inRange, entry.EmployeeId);
    }
}
