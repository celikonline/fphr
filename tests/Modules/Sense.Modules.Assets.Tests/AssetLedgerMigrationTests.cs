using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Assets.Infrastructure;
using Sense.Modules.Assets.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>
/// The AssetLedgerAndDetails migration run against a database that is still on the previous schema and already holds
/// zimmet rows (real PostgreSQL, see TestFixture): the backfill must give every currently Assigned asset exactly one
/// open ledger row carrying that asset's own tenant, and every existing asset the "Good" condition.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AssetLedgerMigrationTests : TestFixture
{
    private const string PreviousMigration = "20260923163844_AssetAssignmentApproval";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AssetsDbContext>(configuration, AssetsDbContext.SchemaName);
        services.AddAssetsCoreServices();
    }

    /// <summary>Stops one migration short of the ledger so the test can seed "v1" data first.</summary>
    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AssetsDbContext>().GetService<IMigrator>().MigrateAsync(PreviousMigration);

    private static async Task InsertV1AssetAsync(
        AssetsDbContext db, Guid id, Guid tenantId, string status, Guid? employeeId, DateOnly? issuedAt) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO assets.assets (id, tenant_id, category_id, name, status, assigned_employee_id, issued_at, created_at)
            VALUES ({id}, {tenantId}, {Guid.NewGuid()}, {"Item " + id}, {status}, {employeeId}, {issuedAt}, now())
            """);

    [Fact]
    public async Task Migrate_GivesEveryAssignedAssetOneOpenLedgerRowWithItsOwnTenant()
    {
        var assignedA = Guid.NewGuid();
        var assignedB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var spare = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var retired = Guid.NewGuid();

        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        await InsertV1AssetAsync(db, assignedA, TenantA, "Assigned", employeeA, new DateOnly(2026, 3, 1));
        await InsertV1AssetAsync(db, assignedB, TenantB, "Assigned", employeeB, new DateOnly(2026, 4, 2));
        await InsertV1AssetAsync(db, spare, TenantA, "Unassigned", null, null);
        await InsertV1AssetAsync(db, pending, TenantA, "PendingAssignment", null, null);
        await InsertV1AssetAsync(db, retired, TenantA, "Retired", null, null);

        await db.Database.MigrateAsync();

        var rows = await db.AssetAssignments.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        rows.Count.ShouldBe(2);

        var rowA = rows.Single(r => r.AssetId == assignedA);
        rowA.TenantId.ShouldBe(TenantA);
        rowA.EmployeeId.ShouldBe(employeeA);
        rowA.IssuedAt.ShouldBe(new DateOnly(2026, 3, 1));
        rowA.IsOpen.ShouldBeTrue();
        rowA.AcknowledgedAt.ShouldBeNull();

        var rowB = rows.Single(r => r.AssetId == assignedB);
        rowB.TenantId.ShouldBe(TenantB);
        rowB.EmployeeId.ShouldBe(employeeB);
        rowB.IssuedAt.ShouldBe(new DateOnly(2026, 4, 2));
        rowB.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Migrate_BackfilledAssetsLoadWithTheirOpenAssignmentAndGoodCondition()
    {
        var assignedA = Guid.NewGuid();
        var spare = Guid.NewGuid();
        var employeeA = Guid.NewGuid();

        using (var seed = ServiceProvider!.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AssetsDbContext>();
            await InsertV1AssetAsync(db, assignedA, TenantA, "Assigned", employeeA, new DateOnly(2026, 3, 1));
            await InsertV1AssetAsync(db, spare, TenantA, "Unassigned", null, null);
            await db.Database.MigrateAsync();
        }

        using var tenantScope = TenantContextSetter!.BeginScope(TenantA);
        using var scope = ServiceProvider!.CreateScope();
        var assets = scope.ServiceProvider.GetRequiredService<Sense.Modules.Assets.Domain.IAssetRepository>();

        var assigned = (await assets.GetByIdAsync(assignedA)).Value;
        assigned.Condition.ShouldBe(AssetCondition.Good);
        assigned.OpenAssignment.ShouldNotBeNull().EmployeeId.ShouldBe(employeeA);

        // The backfilled row is a real ledger row: returning the asset closes it.
        assigned.Return(new DateOnly(2026, 9, 1), AssetCondition.Fair, "ok").IsSuccess.ShouldBeTrue();
        await scope.ServiceProvider.GetRequiredService<AssetsDbContext>().SaveChangesAsync();

        var history = await assets.ListAssignmentsAsync(assignedA);
        history.ShouldHaveSingleItem().EndReason.ShouldBe(AssignmentEndReason.Returned);

        (await assets.GetByIdAsync(spare)).Value.Condition.ShouldBe(AssetCondition.Good);
    }

    [Fact]
    public async Task Migrate_ThenDown_RevertsCleanlyAndCanBeReapplied()
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        await InsertV1AssetAsync(db, Guid.NewGuid(), TenantA, "Assigned", Guid.NewGuid(), new DateOnly(2026, 3, 1));
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotContain(m => m.EndsWith("_AssetLedgerAndDetails", StringComparison.Ordinal));

        await db.Database.MigrateAsync();
        (await db.AssetAssignments.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }
}
