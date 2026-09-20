using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Unit tests for the HolidayCalendar/HolidayCalendarEntry domain logic (roadmap Faz 0 item #5),
/// in particular HolidayCalendarEntry.MatchesDate - the core of the "Date vs MonthDay" design decision
/// documented on HolidayCalendarEntry: a single Date column plus IsRecurringAnnually, where a recurring
/// entry matches every year on the same month/day and a non-recurring entry matches only its exact date.
/// </summary>
public sealed class HolidayCalendarTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidNameAndCountryCode_Succeeds()
    {
        var result = HolidayCalendar.Create(TenantId, "Türkiye Resmi Tatilleri", "tr", isSystemDefined: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Türkiye Resmi Tatilleri");
        result.Value.CountryCode.ShouldBe("TR"); // normalized to upper-invariant
        result.Value.IsSystemDefined.ShouldBeTrue();
        result.Value.TenantId.ShouldBe(TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyName_Fails(string? name)
    {
        var result = HolidayCalendar.Create(TenantId, name!, "TR", isSystemDefined: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarNameRequired);
    }

    [Fact]
    public void Create_WithEmptyCountryCode_Fails()
    {
        var result = HolidayCalendar.Create(TenantId, "Custom Calendar", "  ", isSystemDefined: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarCountryCodeRequired);
    }

    [Fact]
    public void CreateEntry_WithEmptyCalendarId_Fails()
    {
        var result = HolidayCalendarEntry.Create(TenantId, Guid.Empty, new DateOnly(2000, 1, 1), "Yılbaşı", isRecurringAnnually: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarEntryCalendarIdRequired);
    }

    [Fact]
    public void CreateEntry_WithEmptyName_Fails()
    {
        var result = HolidayCalendarEntry.Create(TenantId, Guid.NewGuid(), new DateOnly(2000, 1, 1), " ", isRecurringAnnually: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarEntryNameRequired);
    }

    [Fact]
    public void MatchesDate_RecurringEntry_MatchesSameMonthDayInAnyYear()
    {
        // Anchor year is arbitrary for a recurring entry (see class doc comment) - 2000 here, as
        // Sense.Api.SeedData.DataSeeder's seeder uses for the TR calendar.
        var entryResult = HolidayCalendarEntry.Create(
            TenantId, Guid.NewGuid(), new DateOnly(2000, 10, 29), "Cumhuriyet Bayramı", isRecurringAnnually: true);
        entryResult.IsSuccess.ShouldBeTrue();
        var entry = entryResult.Value;

        entry.MatchesDate(new DateOnly(2026, 10, 29)).ShouldBeTrue();
        entry.MatchesDate(new DateOnly(2031, 10, 29)).ShouldBeTrue();
        entry.MatchesDate(new DateOnly(2026, 10, 28)).ShouldBeFalse();
        entry.MatchesDate(new DateOnly(2026, 11, 29)).ShouldBeFalse();
    }

    [Fact]
    public void MatchesDate_NonRecurringEntry_MatchesOnlyExactDate()
    {
        // A shifting religious holiday: exact date for one specific year, per HolidayCalendarEntry's
        // documented design (no lunar-calendar computation anywhere in this codebase).
        var entryResult = HolidayCalendarEntry.Create(
            TenantId, Guid.NewGuid(), new DateOnly(2027, 3, 9), "Ramazan Bayramı (1. gün)", isRecurringAnnually: false);
        entryResult.IsSuccess.ShouldBeTrue();
        var entry = entryResult.Value;

        entry.MatchesDate(new DateOnly(2027, 3, 9)).ShouldBeTrue();
        entry.MatchesDate(new DateOnly(2028, 3, 9)).ShouldBeFalse(); // same month/day, different (and correct) year - must NOT match
        entry.MatchesDate(new DateOnly(2027, 3, 10)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("XX")]
    [InlineData("T")]
    [InlineData("TUR")]
    [InlineData("1A")]
    public void Create_WithCountryCodeOutsideIso3166_Fails(string countryCode)
    {
        var result = HolidayCalendar.Create(TenantId, "Custom Calendar", countryCode, isSystemDefined: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarCountryCodeInvalid);
    }

    [Theory]
    [InlineData("TR", "TR")]
    [InlineData("de", "DE")]
    [InlineData(" us ", "US")]
    public void Create_WithIsoCountryCode_NormalizesToUpperCase(string input, string expected)
    {
        var result = HolidayCalendar.Create(TenantId, "Custom Calendar", input, isSystemDefined: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CountryCode.ShouldBe(expected);
    }

    [Fact]
    public void Rename_CustomCalendar_ChangesName()
    {
        var calendar = HolidayCalendar.Create(TenantId, "Eski ad", "TR", isSystemDefined: false).Value;

        var result = calendar.Rename("  Yeni ad ");

        result.IsSuccess.ShouldBeTrue();
        calendar.Name.ShouldBe("Yeni ad");
    }

    [Fact]
    public void Rename_SystemCalendar_Fails()
    {
        var calendar = HolidayCalendar.Create(TenantId, "Türkiye Resmi Tatilleri", "TR", isSystemDefined: true).Value;

        var result = calendar.Rename("Başka ad");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarSystemImmutable);
        calendar.Name.ShouldBe("Türkiye Resmi Tatilleri");
    }

    [Fact]
    public void Rename_WithEmptyName_Fails()
    {
        var calendar = HolidayCalendar.Create(TenantId, "Eski ad", "TR", isSystemDefined: false).Value;

        var result = calendar.Rename("  ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarNameRequired);
    }

    [Fact]
    public void CreateEntry_DefaultsToFullDay_AndCanBeFlaggedHalfDay()
    {
        var full = HolidayCalendarEntry.Create(TenantId, Guid.NewGuid(), new DateOnly(2000, 10, 29), "Cumhuriyet Bayramı", isRecurringAnnually: true).Value;
        var half = HolidayCalendarEntry.Create(
            TenantId, Guid.NewGuid(), new DateOnly(2000, 10, 28), "Cumhuriyet Bayramı Arifesi", isRecurringAnnually: true, isHalfDay: true).Value;

        full.IsHalfDay.ShouldBeFalse();
        half.IsHalfDay.ShouldBeTrue();
    }

    [Fact]
    public void UpdateEntry_ChangesAllEditableFields()
    {
        var entry = HolidayCalendarEntry.Create(TenantId, Guid.NewGuid(), new DateOnly(2027, 3, 9), "Eski", isRecurringAnnually: false).Value;

        var result = entry.Update(new DateOnly(2000, 10, 28), "  Arife ", isRecurringAnnually: true, isHalfDay: true);

        result.IsSuccess.ShouldBeTrue();
        entry.Date.ShouldBe(new DateOnly(2000, 10, 28));
        entry.Name.ShouldBe("Arife");
        entry.IsRecurringAnnually.ShouldBeTrue();
        entry.IsHalfDay.ShouldBeTrue();
    }

    [Fact]
    public void UpdateEntry_WithEmptyName_Fails_AndKeepsState()
    {
        var entry = HolidayCalendarEntry.Create(TenantId, Guid.NewGuid(), new DateOnly(2027, 3, 9), "Eski", isRecurringAnnually: false).Value;

        var result = entry.Update(new DateOnly(2027, 3, 10), " ", isRecurringAnnually: false, isHalfDay: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarEntryNameRequired);
        entry.Date.ShouldBe(new DateOnly(2027, 3, 9));
        entry.Name.ShouldBe("Eski");
    }
}
