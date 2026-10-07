using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Application.Import;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Assets.Infrastructure;
using Sense.Modules.Assets.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>
/// Zimmet v2 persistence on real PostgreSQL: ledger writes, the open-row-only loading, tag uniqueness, search,
/// history/summary reads, Excel-import ledger, and strict tenant isolation of everything new.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AssetLedgerIntegrationTests : TestFixture
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid EmployeeA = Guid.NewGuid();
    private static readonly Guid EmployeeB = Guid.NewGuid();
    private static readonly DateOnly Issued = new(2026, 9, 1);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AssetsDbContext>(configuration, AssetsDbContext.SchemaName);
        services.AddAssetsCoreServices();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AssetsDbContext>().Database.MigrateAsync();

    /// <summary>Runs <paramref name="action"/> in a fresh DI scope as <paramref name="tenantId"/> and saves afterwards.</summary>
    private async Task<T> AsTenantAsync<T>(Guid tenantId, Func<IAssetRepository, AssetsDbContext, Task<T>> action)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var result = await action(scope.ServiceProvider.GetRequiredService<IAssetRepository>(), db);
        await db.SaveChangesAsync();
        return result;
    }

    private Task<Guid> SeedAsync(
        Guid tenantId, string name, string? serial = null, AssetDetails? details = null, Guid? assignTo = null, DateOnly? issued = null) =>
        AsTenantAsync(tenantId, (assets, _) =>
        {
            var asset = Asset.Create(tenantId, CategoryId, name, serial, null, details).Value;
            if (assignTo is { } employee)
            {
                asset.Assign(employee, issued ?? Issued).IsSuccess.ShouldBeTrue();
            }

            assets.Add(asset);
            return Task.FromResult(asset.Id);
        });

    private static ICurrentUser User(Guid employeeId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.EmployeeId.Returns(employeeId);
        return user;
    }

    // ---- ledger persistence ----

    [Fact]
    public async Task AssignThenReturn_PersistsOpenThenClosedLedgerRow()
    {
        var id = await SeedAsync(TenantA, "MacBook", "SN-1", assignTo: EmployeeA);

        var open = await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAssignmentsAsync(id));
        var openRow = open.ShouldHaveSingleItem();
        openRow.IsOpen.ShouldBeTrue();
        openRow.EmployeeId.ShouldBe(EmployeeA);
        openRow.TenantId.ShouldBe(TenantA);

        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            var asset = (await assets.GetByIdAsync(id)).Value;
            asset.Return(Issued.AddDays(9), AssetCondition.Damaged, "ekran").IsSuccess.ShouldBeTrue();
            return 0;
        });

        var closed = (await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAssignmentsAsync(id))).ShouldHaveSingleItem();
        closed.ReturnedAt.ShouldBe(Issued.AddDays(9));
        closed.EndReason.ShouldBe(AssignmentEndReason.Returned);
        closed.ReturnCondition.ShouldBe(AssetCondition.Damaged);
        closed.Note.ShouldBe("ekran");
    }

    [Fact]
    public async Task LostWhileAssigned_PersistsEndReasonLost()
    {
        var id = await SeedAsync(TenantA, "Telefon", assignTo: EmployeeA);

        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            (await assets.GetByIdAsync(id)).Value.MarkLost(Issued.AddDays(2), "kayıp").IsSuccess.ShouldBeTrue();
            return 0;
        });

        var row = (await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAssignmentsAsync(id))).ShouldHaveSingleItem();
        row.EndReason.ShouldBe(AssignmentEndReason.Lost);
        row.ReturnCondition.ShouldBeNull();
        (await AsTenantAsync(TenantA, async (assets, _) => (await assets.GetByIdAsync(id)).Value.Status)).ShouldBe(AssetStatus.Lost);
    }

    [Fact]
    public async Task AcknowledgePersists_AndOnlyTheOpenRowIsLoadedForMutation()
    {
        var id = await SeedAsync(TenantA, "Laptop", assignTo: EmployeeA);
        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            var asset = (await assets.GetByIdAsync(id)).Value;
            asset.Return(Issued.AddDays(1)).IsSuccess.ShouldBeTrue();
            return 0;
        });
        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            var asset = (await assets.GetByIdAsync(id)).Value;
            asset.Assign(EmployeeB, Issued.AddDays(5)).IsSuccess.ShouldBeTrue();
            return 0;
        });

        var loaded = await AsTenantAsync(TenantA, async (assets, _) =>
        {
            var asset = (await assets.GetByIdAsync(id)).Value;
            var count = asset.Assignments.Count;
            asset.Acknowledge(EmployeeB, new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc)).IsSuccess.ShouldBeTrue();
            return count;
        });

        loaded.ShouldBe(1);
        var ledger = await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAssignmentsAsync(id));
        ledger.Count.ShouldBe(2);
        ledger[0].EmployeeId.ShouldBe(EmployeeB);
        ledger[0].AcknowledgedAt.ShouldBe(new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc));
        ledger[1].EmployeeId.ShouldBe(EmployeeA);
        ledger[1].AcknowledgedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ReturnAndReassignInOneSave_DoesNotTripTheSingleOpenRowIndex()
    {
        var id = await SeedAsync(TenantA, "Laptop", assignTo: EmployeeA);

        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            var asset = (await assets.GetByIdAsync(id)).Value;
            asset.Return(Issued.AddDays(1)).IsSuccess.ShouldBeTrue();
            asset.Assign(EmployeeB, Issued.AddDays(1)).IsSuccess.ShouldBeTrue();
            return 0;
        });

        var ledger = await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAssignmentsAsync(id));
        ledger.Count.ShouldBe(2);
        ledger.Count(r => r.IsOpen).ShouldBe(1);
    }

    [Fact]
    public async Task TwoOpenRowsForOneAsset_AreRejectedByTheDatabase()
    {
        var id = await SeedAsync(TenantA, "Laptop", assignTo: EmployeeA);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        var insert = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO assets.asset_assignments (id, tenant_id, asset_id, employee_id, issued_at, created_at) VALUES ({Guid.NewGuid()}, {TenantA}, {id}, {EmployeeB}, {Issued}, now())");

        await insert.ShouldThrowAsync<Exception>();
    }

    // ---- demirbaş no ----

    [Fact]
    public async Task IsAssetTagTakenAsync_IsCaseInsensitiveTrimmedAndExcludesItself()
    {
        var id = await SeedAsync(TenantA, "Laptop", details: new AssetDetails("DMB-001", null, null, null, null, null));

        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            (await assets.IsAssetTagTakenAsync("dmb-001", null)).ShouldBeTrue();
            (await assets.IsAssetTagTakenAsync("  DMB-001  ", null)).ShouldBeTrue();
            (await assets.IsAssetTagTakenAsync("DMB-001", id)).ShouldBeFalse();
            (await assets.IsAssetTagTakenAsync("DMB-001", Guid.NewGuid())).ShouldBeTrue();
            (await assets.IsAssetTagTakenAsync("DMB-002", null)).ShouldBeFalse();
            return 0;
        });
    }

    [Fact]
    public async Task DuplicateTagInOneTenant_IsRejectedByTheUniqueIndex_ManyNullTagsAreFine()
    {
        await SeedAsync(TenantA, "A", details: new AssetDetails("DMB-001", null, null, null, null, null));
        await SeedAsync(TenantA, "B");
        await SeedAsync(TenantA, "C");

        var duplicate = () => SeedAsync(TenantA, "D", details: new AssetDetails("DMB-001", null, null, null, null, null));

        await duplicate.ShouldThrowAsync<DbUpdateException>();
    }

    // ---- search ----

    [Fact]
    public async Task ListAndCount_SearchMatchesNameSerialTagBrandModelCaseInsensitively_AndWildcardsAreLiteral()
    {
        await SeedAsync(TenantA, "MacBook Pro", "SN-AAA");
        await SeedAsync(TenantA, "Telefon", "SN-BBB", new AssetDetails("DMB-77", "Samsung", "Galaxy S24", null, null, null));
        await SeedAsync(TenantA, "Yüzde 100% kasa");
        await SeedAsync(TenantA, "Alt_çizgi");

        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            async Task<int> Matches(string term)
            {
                var list = await assets.ListAsync(null, null, null, term, 0, 50);
                (await assets.CountAsync(null, null, null, term)).ShouldBe(list.Count);
                return list.Count;
            }

            (await Matches("macbook")).ShouldBe(1);
            (await Matches("sn-bbb")).ShouldBe(1);
            (await Matches("dmb-77")).ShouldBe(1);
            (await Matches("SAMSUNG")).ShouldBe(1);
            (await Matches("galaxy")).ShouldBe(1);
            (await Matches("100%")).ShouldBe(1);
            (await Matches("%")).ShouldBe(1);
            (await Matches("_")).ShouldBe(1);
            (await Matches("yok-böyle")).ShouldBe(0);
            (await Matches("  ")).ShouldBe(4);
            (await assets.ListAsync(null, null, null, null, 0, 50)).Count.ShouldBe(4);
            return 0;
        });
    }

    [Fact]
    public async Task ListAsync_LoadsOnlyTheOpenAssignmentForTheListedAssets()
    {
        var held = await SeedAsync(TenantA, "Held", assignTo: EmployeeA);
        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            var asset = (await assets.GetByIdAsync(held)).Value;
            asset.Return(Issued.AddDays(1));
            asset.Assign(EmployeeB, Issued.AddDays(2));
            asset.Acknowledge(EmployeeB, DateTime.UtcNow);
            return 0;
        });

        var listed = await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAsync(null, null, null, null, 0, 10));

        var asset = listed.ShouldHaveSingleItem();
        asset.Assignments.ShouldHaveSingleItem().EmployeeId.ShouldBe(EmployeeB);
        asset.OpenAssignment!.AcknowledgedAt.ShouldNotBeNull();
    }

    // ---- history / summary ----

    [Fact]
    public async Task ListEmployeeHistoryAsync_ReturnsOpenAndClosedNewestFirstWithTheAsset()
    {
        var older = await SeedAsync(TenantA, "Eski telefon", "SN-OLD", new AssetDetails("DMB-OLD", null, null, null, null, null), EmployeeA, new DateOnly(2026, 1, 5));
        var newer = await SeedAsync(TenantA, "Yeni laptop", "SN-NEW", assignTo: EmployeeA, issued: new DateOnly(2026, 6, 1));
        await SeedAsync(TenantA, "Başkasının", assignTo: EmployeeB);
        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            (await assets.GetByIdAsync(older)).Value.Return(new DateOnly(2026, 3, 1), AssetCondition.Fair).IsSuccess.ShouldBeTrue();
            return 0;
        });

        var history = await AsTenantAsync(TenantA, async (assets, _) => await assets.ListEmployeeHistoryAsync(EmployeeA));

        history.Select(h => h.Asset.Id).ShouldBe([newer, older]);
        history[0].Assignment.IsOpen.ShouldBeTrue();
        history[1].Assignment.ReturnCondition.ShouldBe(AssetCondition.Fair);
        history[1].Asset.AssetTag.ShouldBe("DMB-OLD");
    }

    [Fact]
    public async Task SummaryRows_ReflectStatusAcknowledgementAndWarranty()
    {
        var ack = await SeedAsync(TenantA, "Onaylı", assignTo: EmployeeA);
        await SeedAsync(TenantA, "Onaysız", assignTo: EmployeeB);
        await SeedAsync(TenantA, "Garantili", details: new AssetDetails(null, null, null, null, null, new DateOnly(2026, 11, 1)));
        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            (await assets.GetByIdAsync(ack)).Value.Acknowledge(EmployeeA, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            return 0;
        });

        var rows = await AsTenantAsync(TenantA, async (assets, _) => await assets.ListSummaryRowsAsync());

        rows.Count.ShouldBe(3);
        rows.Count(r => r.Status == AssetStatus.Assigned && r.Acknowledged).ShouldBe(1);
        rows.Count(r => r.Status == AssetStatus.Assigned && !r.Acknowledged).ShouldBe(1);
        rows.Single(r => r.WarrantyEndsOn is not null).WarrantyEndsOn.ShouldBe(new DateOnly(2026, 11, 1));
    }

    // ---- Excel import fills the ledger through the domain methods ----

    [Fact]
    public async Task ImportRows_OpenRowCreatesOpenAssignment_ClosedRowCreatesClosedAssignment()
    {
        const string nationalId = "10000000146";
        using var tenantScope = TenantContextSetter!.BeginScope(TenantA);
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var employees = Substitute.For<IEmployeeLookup>();
        employees.FindActiveByNationalIdAsync(TenantA, nationalId, Arg.Any<CancellationToken>()).Returns(EmployeeA);
        var handler = new AssetImportRowHandler(
            TenantContextAccessor!, employees,
            scope.ServiceProvider.GetRequiredService<IAssetCategoryRepository>(),
            scope.ServiceProvider.GetRequiredService<IAssetRepository>(), db);
        var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AssetImportColumns.NationalId],
        [
            ImportRowFor(2, nationalId, "Bilgisayar", "SN-OPEN", "01.09.2026", null),
            ImportRowFor(3, nationalId, "Telefon", "SN-CLOSED", "01.03.2026", "15.08.2026"),
        ]);

        var result = await handler.ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows.ShouldAllBe(r => r.Outcome == ImportRowOutcome.Inserted);

        var history = await scope.ServiceProvider.GetRequiredService<IAssetRepository>().ListEmployeeHistoryAsync(EmployeeA);
        history.Count.ShouldBe(2);
        var open = history.Single(h => h.Asset.SerialNumber == "SN-OPEN");
        open.Assignment.IsOpen.ShouldBeTrue();
        open.Assignment.IssuedAt.ShouldBe(new DateOnly(2026, 9, 1));
        var closed = history.Single(h => h.Asset.SerialNumber == "SN-CLOSED");
        closed.Assignment.IsOpen.ShouldBeFalse();
        closed.Assignment.IssuedAt.ShouldBe(new DateOnly(2026, 3, 1));
        closed.Assignment.ReturnedAt.ShouldBe(new DateOnly(2026, 8, 15));
        closed.Assignment.EndReason.ShouldBe(AssignmentEndReason.Returned);
    }

    [Fact]
    public async Task ImportRow_ReturningAnExistingAssignedAsset_ClosesItsPersistedOpenRow()
    {
        const string nationalId = "10000000146";
        var categoryId = await AsTenantAsync(TenantA, (_, db) =>
        {
            var category = AssetCategory.Create(TenantA, "telefon", "Telefon").Value;
            db.AssetCategories.Add(category);
            return Task.FromResult(category.Id);
        });
        var assetId = await AsTenantAsync(TenantA, (assets, _) =>
        {
            var asset = Asset.Create(TenantA, categoryId, "iPhone", "SN-9", null).Value;
            asset.Assign(EmployeeA, new DateOnly(2026, 3, 1));
            assets.Add(asset);
            return Task.FromResult(asset.Id);
        });

        using (var tenantScope = TenantContextSetter!.BeginScope(TenantA))
        using (var scope = ServiceProvider!.CreateScope())
        {
            var employees = Substitute.For<IEmployeeLookup>();
            employees.FindActiveByNationalIdAsync(TenantA, nationalId, Arg.Any<CancellationToken>()).Returns(EmployeeA);
            var handler = new AssetImportRowHandler(
                TenantContextAccessor!, employees,
                scope.ServiceProvider.GetRequiredService<IAssetCategoryRepository>(),
                scope.ServiceProvider.GetRequiredService<IAssetRepository>(),
                scope.ServiceProvider.GetRequiredService<AssetsDbContext>());
            var batch = new ImportBatch(ImportMode.Upsert, ImportFailurePolicy.SkipInvalidRows, [AssetImportColumns.NationalId],
                [ImportRowFor(2, nationalId, "Telefon", "SN-9", "01.03.2026", "15.08.2026")]);

            var result = await handler.ProcessAsync(batch, TestContext.Current.CancellationToken);

            result.Rows.Single().Outcome.ShouldBe(ImportRowOutcome.Updated);
        }

        var ledger = await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAssignmentsAsync(assetId));
        var row = ledger.ShouldHaveSingleItem();
        row.IsOpen.ShouldBeFalse();
        row.ReturnedAt.ShouldBe(new DateOnly(2026, 8, 15));
    }

    private static ImportRow ImportRowFor(int rowNumber, string nationalId, string category, string serial, string assignedAt, string? returnedAt)
    {
        var values = new Dictionary<string, ImportCell>
        {
            [AssetImportColumns.NationalId] = new(nationalId),
            [AssetImportColumns.CategoryName] = new(category),
            [AssetImportColumns.SerialNumber] = new(serial),
            [AssetImportColumns.AssignedAt] = new(assignedAt),
        };
        if (returnedAt is not null)
        {
            values[AssetImportColumns.ReturnedAt] = new(returnedAt);
        }

        return new ImportRow(rowNumber, values);
    }

    // ---- tenant isolation of everything v2 added ----

    [Fact]
    public async Task TenantB_CannotSee_TenantAs_Assets_History_Summary_Or_SearchHits()
    {
        var assetId = await SeedAsync(
            TenantA, "A'nın laptopu", "SN-A", new AssetDetails("DMB-001", "Apple", "M3", null, 10m, new DateOnly(2026, 11, 1)), EmployeeA);

        await AsTenantAsync(TenantB, async (assets, db) =>
        {
            (await assets.GetByIdAsync(assetId)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await assets.FindByIdAsync(assetId)).ShouldBeNull();
            (await assets.ListAsync(null, null, null, null, 0, 50)).ShouldBeEmpty();
            (await assets.CountAsync(null, null, null, null)).ShouldBe(0);
            (await assets.ListAsync(null, null, null, "dmb-001", 0, 50)).ShouldBeEmpty();
            (await assets.ListAsync(null, null, null, "apple", 0, 50)).ShouldBeEmpty();
            (await assets.ListByEmployeeAsync(EmployeeA)).ShouldBeEmpty();
            (await assets.ListAssignmentsAsync(assetId)).ShouldBeEmpty();
            (await assets.ListEmployeeHistoryAsync(EmployeeA)).ShouldBeEmpty();
            (await assets.ListSummaryRowsAsync()).ShouldBeEmpty();
            (await assets.IsAssetTagTakenAsync("DMB-001", null)).ShouldBeFalse();
            (await assets.HasOpenAssetsAsync(TenantB, EmployeeA)).ShouldBeFalse();
            (await db.AssetAssignments.CountAsync()).ShouldBe(0);
            return 0;
        });

        // ...while tenant A still sees all of it.
        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            (await assets.ListAssignmentsAsync(assetId)).ShouldHaveSingleItem();
            (await assets.ListEmployeeHistoryAsync(EmployeeA)).ShouldHaveSingleItem();
            (await assets.ListSummaryRowsAsync()).ShouldHaveSingleItem();
            (await assets.IsAssetTagTakenAsync("DMB-001", null)).ShouldBeTrue();
            return 0;
        });
    }

    [Fact]
    public async Task SummaryHandler_CountsOnlyTheCallersTenant()
    {
        await SeedAsync(TenantA, "A1", assignTo: EmployeeA);
        await SeedAsync(TenantA, "A2");
        await SeedAsync(TenantB, "B1");

        async Task<AssetSummaryDto> SummaryFor(Guid tenantId) => await AsTenantAsync(tenantId, async (assets, db) =>
        {
            var handler = new AssetSummaryHandler(assets, new AssetCategoryRepository(db), TimeProvider.System);
            return (await handler.Handle(new GetAssetSummaryQuery(), CancellationToken.None)).Value;
        });

        var a = await SummaryFor(TenantA);
        var b = await SummaryFor(TenantB);

        a.Total.ShouldBe(2);
        a.Assigned.ShouldBe(1);
        a.Unacknowledged.ShouldBe(1);
        b.Total.ShouldBe(1);
        b.Assigned.ShouldBe(0);
        b.Unassigned.ShouldBe(1);
    }

    [Fact]
    public async Task SameAssetTag_IsFreeInAnotherTenant_ButStillUniqueInsideEach()
    {
        var tag = new AssetDetails("DMB-001", null, null, null, null, null);
        await SeedAsync(TenantA, "A laptop", details: tag);

        var inB = await SeedAsync(TenantB, "B laptop", details: tag);

        inB.ShouldNotBe(Guid.Empty);
        (await AsTenantAsync(TenantA, async (assets, _) => await assets.ListAsync(null, null, null, "DMB-001", 0, 10))).ShouldHaveSingleItem().Name.ShouldBe("A laptop");
        (await AsTenantAsync(TenantB, async (assets, _) => await assets.ListAsync(null, null, null, "DMB-001", 0, 10))).ShouldHaveSingleItem().Name.ShouldBe("B laptop");
        var duplicateInB = () => SeedAsync(TenantB, "B second", details: tag);
        await duplicateInB.ShouldThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CreateHandler_AllowsATagTakenInAnotherTenant_ButConflictsInTheSameTenant()
    {
        await SeedAsync(TenantA, "A laptop", details: new AssetDetails("DMB-001", null, null, null, null, null));

        async Task<Result<Guid>> CreateAs(Guid tenantId)
        {
            var categoryId = await AsTenantAsync(tenantId, (_, db) =>
            {
                var category = AssetCategory.Create(tenantId, "c" + Guid.NewGuid().ToString("N")[..6], "Kat").Value;
                db.AssetCategories.Add(category);
                return Task.FromResult(category.Id);
            });
            return await AsTenantAsync(tenantId, async (assets, db) =>
            {
                var tenant = Substitute.For<ITenantContext>();
                tenant.TenantId.Returns(tenantId);
                var handler = new CreateAssetHandler(tenant, assets, new AssetCategoryRepository(db));
                return await handler.Handle(new CreateAssetCommand(categoryId, "Laptop", null, null, AssetTag: "dmb-001"), CancellationToken.None);
            });
        }

        (await CreateAs(TenantB)).IsSuccess.ShouldBeTrue();
        var again = await CreateAs(TenantA);
        again.Error.Code.ShouldBe(AssetsDomainErrors.TagInUse);
    }

    [Fact]
    public async Task TenantB_UserCannotAcknowledgeReturnOrLoseTenantAsAsset()
    {
        var assetId = await SeedAsync(TenantA, "A laptop", assignTo: EmployeeA);

        // The worst case: tenant B's user is linked to an employee that carries the very same id as A's assignee.
        var (acknowledge, returned, lost) = await AsTenantAsync(TenantB, async (assets, _) =>
        {
            var ack = await new AcknowledgeAssetHandler(User(EmployeeA), assets, TimeProvider.System)
                .Handle(new AcknowledgeAssetCommand(assetId), CancellationToken.None);
            var ret = await new ReturnAssetHandler(assets, Substitute.For<IIntegrationEventOutbox>())
                .Handle(new ReturnAssetCommand(assetId, Issued.AddDays(3), AssetCondition.Damaged, "x"), CancellationToken.None);
            var loss = await new MarkAssetLostHandler(assets)
                .Handle(new MarkAssetLostCommand(assetId, Issued.AddDays(3)), CancellationToken.None);
            return (ack, ret, loss);
        });

        acknowledge.Error.Type.ShouldBe(ErrorType.NotFound);
        acknowledge.Error.Code.ShouldBe(AssetsDomainErrors.NotFound);
        returned.Error.Type.ShouldBe(ErrorType.NotFound);
        lost.Error.Type.ShouldBe(ErrorType.NotFound);

        // Tenant A's asset is untouched: still assigned, the single open row neither acknowledged nor closed.
        await AsTenantAsync(TenantA, async (assets, _) =>
        {
            var asset = (await assets.GetByIdAsync(assetId)).Value;
            asset.Status.ShouldBe(AssetStatus.Assigned);
            asset.Condition.ShouldBe(AssetCondition.Good);
            var row = asset.OpenAssignment.ShouldNotBeNull();
            row.AcknowledgedAt.ShouldBeNull();
            (await assets.ListAssignmentsAsync(assetId)).ShouldHaveSingleItem().IsOpen.ShouldBeTrue();
            return 0;
        });
    }

    [Fact]
    public async Task TenantA_AssigneeCanStillAcknowledgeInOwnTenant_AfterTenantBAttempt()
    {
        var assetId = await SeedAsync(TenantA, "A laptop", assignTo: EmployeeA);

        var asOwner = await AsTenantAsync(TenantA, async (assets, _) =>
            await new AcknowledgeAssetHandler(User(EmployeeA), assets, TimeProvider.System).Handle(new AcknowledgeAssetCommand(assetId), CancellationToken.None));
        var asOtherEmployee = await AsTenantAsync(TenantA, async (assets, _) =>
            await new AcknowledgeAssetHandler(User(EmployeeB), assets, TimeProvider.System).Handle(new AcknowledgeAssetCommand(assetId), CancellationToken.None));

        asOwner.IsSuccess.ShouldBeTrue();
        asOtherEmployee.IsFailure.ShouldBeTrue();
        (await AsTenantAsync(TenantA, async (assets, _) => (await assets.ListAssignmentsAsync(assetId)).Single().AcknowledgedAt)).ShouldNotBeNull();
    }
}
