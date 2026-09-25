using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>E4: legacy EventDate/enum rows survive the migration; the range query returns overlapping events.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class CompanyEventMigrationIntegrationTests : TestFixture
{
    private const string PreviousMigration = "20260923114844_AddRecognitions";

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AnnouncementsDbContext>(configuration, AnnouncementsDbContext.SchemaName);
    }

    [Fact]
    public async Task Legacy_event_migrates_to_all_day_with_same_date()
    {
        var db = ServiceProvider!.GetRequiredService<AnnouncementsDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO announcements.company_events (id, title, event_date, type, created_by_user_id, created_at, tenant_id)
            VALUES ({id}, 'Eski etkinlik', DATE '2026-09-10', 'Meeting', {Guid.NewGuid()}, now(), {tenantId})");

        await migrator.MigrateAsync();

        using var scope = TenantContextSetter!.BeginScope(tenantId);
        var migrated = await db.CompanyEvents.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.True(migrated.IsAllDay);
        Assert.Equal(new DateOnly(2026, 9, 10), migrated.EventDate);
        Assert.Equal(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), migrated.StartsAt);
        Assert.Equal(CompanyEventType.Meeting, migrated.Type);
        Assert.Null(migrated.Mode);
    }

    [Fact]
    public async Task Repository_returns_only_events_overlapping_range()
    {
        var db = ServiceProvider!.GetRequiredService<AnnouncementsDbContext>();
        await db.Database.MigrateAsync();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        CompanyEvent Make(string title, DateTime start, DateTime? end) => CompanyEvent.Create(
            tenantId, title, null, CompanyEventType.CompanyEvent, null, Guid.NewGuid(), new CompanyEventPlan(start, end, true)).Value;
        db.CompanyEvents.AddRange(
            Make("spans-start", new DateTime(2026, 8, 30), new DateTime(2026, 9, 2)),
            Make("inside", new DateTime(2026, 9, 15), null),
            Make("last-day", new DateTime(2026, 9, 30), null),
            Make("before", new DateTime(2026, 8, 1), new DateTime(2026, 8, 31)),
            Make("after", new DateTime(2026, 10, 1), null));
        await db.SaveChangesAsync();

        var found = await new CompanyEventRepository(db).GetInRangeAsync(tenantId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Equal(["spans-start", "inside", "last-day"], found.Select(e => e.Title));
    }
}
