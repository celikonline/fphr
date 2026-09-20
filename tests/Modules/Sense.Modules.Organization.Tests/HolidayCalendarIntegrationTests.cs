using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Integration test (real PostgreSQL via Testcontainers, see TestFixture) for roadmap Faz 0 item #5:
/// seeds a per-tenant TR-style holiday calendar with both a recurring (fixed-date) and a non-recurring
/// (shifting) entry, assigns it to the tenant's Company, and exercises HolidayCalendarLookup - the same
/// implementation OrganizationModule registers as Contracts.IHolidayCalendarLookup - directly against
/// the DbContext, mirroring OrgUnitIntegrationTests' approach of going through the persistence/domain
/// layer rather than Dispatcher.Send(...) (see that test's doc comment for why).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class HolidayCalendarIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<OrganizationDbContext>(configuration, OrganizationDbContext.SchemaName);
    }

    [Fact]
    public async Task IsHolidayAsync_AndGetUpcomingHolidaysAsync_ResolveViaCompanyCalendar_AndAreTenantIsolated()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        // Seed hierarchy levels + root Company org unit (as DataSeeder does for real tenants).
        var typeResults = OrgUnitTypeCatalog.CreateAll(tenantId);
        foreach (var result in typeResults)
        {
            result.IsSuccess.ShouldBeTrue();
            db.OrgUnitTypes.Add(result.Value);
        }

        var companyTypeId = typeResults.Single(r => r.Value.Code == OrgUnitTypeCodes.Company).Value.Id;
        var rootUnitResult = OrgUnit.Create(tenantId, companyTypeId, parentId: null, name: "Headquarters");
        rootUnitResult.IsSuccess.ShouldBeTrue();
        db.OrgUnits.Add(rootUnitResult.Value);

        // Seed the holiday calendar: one recurring entry (fixed national holiday, anchor year 2000 -
        // arbitrary, see HolidayCalendarEntry's doc comment) and one non-recurring entry (a shifting
        // religious holiday, exact date for one specific year).
        var calendarResult = HolidayCalendar.Create(tenantId, "Türkiye Resmi Tatilleri", "TR", isSystemDefined: true);
        calendarResult.IsSuccess.ShouldBeTrue();
        var calendar = calendarResult.Value;
        db.HolidayCalendars.Add(calendar);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recurringEntryResult = HolidayCalendarEntry.Create(
            tenantId, calendar.Id, new DateOnly(2000, 10, 29), "Cumhuriyet Bayramı", isRecurringAnnually: true);
        recurringEntryResult.IsSuccess.ShouldBeTrue();
        db.HolidayCalendarEntries.Add(recurringEntryResult.Value);

        var farFutureDate = today.AddYears(1); // guaranteed to be "upcoming" regardless of today's date
        var nonRecurringEntryResult = HolidayCalendarEntry.Create(
            tenantId, calendar.Id, farFutureDate, "Kurban Bayramı (1. gün)", isRecurringAnnually: false);
        nonRecurringEntryResult.IsSuccess.ShouldBeTrue();
        db.HolidayCalendarEntries.Add(nonRecurringEntryResult.Value);

        // Assign the calendar to the tenant's Company (the "Şirket eklerken tatil takvimi seçilir" UX).
        var companyResult = Company.Create(tenantId, rootUnitResult.Value.Id, calendar.Id);
        companyResult.IsSuccess.ShouldBeTrue();
        db.Companies.Add(companyResult.Value);

        await db.SaveChangesAsync();

        var lookup = new HolidayCalendarLookup(db);

        // IsHolidayAsync: recurring entry matches this year's Oct 29 regardless of stored anchor year.
        (await lookup.IsHolidayAsync(tenantId, new DateOnly(today.Year, 10, 29))).ShouldBeTrue();
        (await lookup.IsHolidayAsync(tenantId, new DateOnly(today.Year, 10, 28))).ShouldBeFalse();
        (await lookup.IsHolidayAsync(tenantId, farFutureDate)).ShouldBeTrue();

        // GetUpcomingHolidaysAsync: both entries should surface, non-recurring with its exact date.
        var upcoming = await lookup.GetUpcomingHolidaysAsync(tenantId, count: 10);
        upcoming.ShouldContain(h => h.Name == "Kurban Bayramı (1. gün)" && h.Date == farFutureDate);
        upcoming.ShouldContain(h => h.Name == "Cumhuriyet Bayramı");
        upcoming.Select(h => h.Date).ShouldBe(upcoming.Select(h => h.Date).OrderBy(d => d).ToList());

        // Tenant isolation: switching ambient tenant context to a different tenant (which has no
        // calendar/Company at all) must not see this tenant's data - both queries degrade to
        // "no holiday" / "empty list", not an error, and the calendar itself is invisible via the
        // global query filter.
        using (TenantContextSetter!.BeginScope(otherTenantId))
        {
            (await lookup.IsHolidayAsync(otherTenantId, farFutureDate)).ShouldBeFalse();
            (await lookup.GetUpcomingHolidaysAsync(otherTenantId)).ShouldBeEmpty();

            var visibleToOtherTenant = await db.HolidayCalendars.AnyAsync(c => c.Id == calendar.Id);
            visibleToOtherTenant.ShouldBeFalse();
        }

        // GetLastDefinedReligiousHolidayYearAsync (senseik-037): only the non-recurring entry's year counts -
        // the recurring Cumhuriyet Bayramı entry (anchor year 2000) must not leak in as "the calendar covers 2000".
        (await lookup.GetLastDefinedReligiousHolidayYearAsync(tenantId)).ShouldBe(farFutureDate.Year);
        (await lookup.GetLastDefinedReligiousHolidayYearAsync(otherTenantId)).ShouldBeNull();
    }

    /// <summary>
    /// senseik-037: a calendar with only recurring (fixed-date) entries has no religious-holiday coverage to compare
    /// against - GetLastDefinedReligiousHolidayYearAsync must return null ("nothing to warn about"), not throw on an empty MAX.
    /// </summary>
    [Fact]
    public async Task GetLastDefinedReligiousHolidayYearAsync_ReturnsNull_WhenCalendarHasOnlyRecurringEntries()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var calendar = HolidayCalendar.Create(tenantId, "Türkiye Resmi Tatilleri", "TR", isSystemDefined: true).Value;
        db.HolidayCalendars.Add(calendar);
        db.HolidayCalendarEntries.Add(
            HolidayCalendarEntry.Create(tenantId, calendar.Id, new DateOnly(2000, 1, 1), "Yılbaşı", isRecurringAnnually: true).Value);
        await db.SaveChangesAsync();

        (await new HolidayCalendarLookup(db).GetLastDefinedReligiousHolidayYearAsync(tenantId)).ShouldBeNull();
    }

    /// <summary>Audit A4: the half-day flag survives persistence and reaches the cross-module lookup.</summary>
    [Fact]
    public async Task GetHolidaysAsync_ExposesTheHalfDayFlag()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var calendar = HolidayCalendar.Create(tenantId, "Türkiye Resmi Tatilleri", "TR", isSystemDefined: true).Value;
        db.HolidayCalendars.Add(calendar);
        db.HolidayCalendarEntries.Add(HolidayCalendarEntry.Create(
            tenantId, calendar.Id, new DateOnly(2000, 10, 28), "Cumhuriyet Bayramı Arifesi", isRecurringAnnually: true, isHalfDay: true).Value);
        db.HolidayCalendarEntries.Add(HolidayCalendarEntry.Create(
            tenantId, calendar.Id, new DateOnly(2000, 10, 29), "Cumhuriyet Bayramı", isRecurringAnnually: true).Value);
        await db.SaveChangesAsync();

        var holidays = await new HolidayCalendarLookup(db).GetHolidaysAsync(tenantId, calendar.Id, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30));

        holidays.Single(h => h.Date == new DateOnly(2026, 10, 28)).IsHalfDay.ShouldBeTrue();
        holidays.Single(h => h.Date == new DateOnly(2026, 10, 29)).IsHalfDay.ShouldBeFalse();
    }

    /// <summary>Audit A3: removing a calendar drops its entries, and "assigned to a company" is detectable.</summary>
    [Fact]
    public async Task RemoveCalendar_CascadesToEntries_AndAssignmentIsDetected()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var typeResults = OrgUnitTypeCatalog.CreateAll(tenantId);
        foreach (var result in typeResults)
        {
            db.OrgUnitTypes.Add(result.Value);
        }

        var companyTypeId = typeResults.Single(r => r.Value.Code == OrgUnitTypeCodes.Company).Value.Id;
        var unit = OrgUnit.Create(tenantId, companyTypeId, parentId: null, name: "Headquarters").Value;
        db.OrgUnits.Add(unit);

        var assigned = HolidayCalendar.Create(tenantId, "Atanmış", "DE", isSystemDefined: false).Value;
        var free = HolidayCalendar.Create(tenantId, "Boş", "DE", isSystemDefined: false).Value;
        db.HolidayCalendars.AddRange(assigned, free);
        db.HolidayCalendarEntries.Add(HolidayCalendarEntry.Create(tenantId, free.Id, new DateOnly(2026, 11, 15), "QA Özel Tatil", isRecurringAnnually: false).Value);
        db.Companies.Add(Company.Create(tenantId, unit.Id, assigned.Id).Value);
        await db.SaveChangesAsync();

        var calendars = new HolidayCalendarRepository(db);
        (await calendars.IsAssignedToCompanyAsync(assigned.Id)).ShouldBeTrue();
        (await calendars.IsAssignedToCompanyAsync(free.Id)).ShouldBeFalse();

        calendars.Remove(free);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.HolidayCalendars.AnyAsync(c => c.Id == free.Id)).ShouldBeFalse();
        (await db.HolidayCalendarEntries.AnyAsync(e => e.HolidayCalendarId == free.Id)).ShouldBeFalse();
    }
}
