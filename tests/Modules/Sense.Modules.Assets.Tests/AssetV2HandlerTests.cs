using NSubstitute;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>Zimmet v2 application layer: tag uniqueness, return/loss/acknowledge handlers, validators, summary.</summary>
public sealed class AssetV2HandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Issued = new(2026, 9, 1);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ITenantContext TenantContext()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static IAssetRepository RepositoryHolding(Asset asset)
    {
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        return assets;
    }

    private static IAssetCategoryRepository KnownCategory()
    {
        var categories = Substitute.For<IAssetCategoryRepository>();
        categories.GetByIdAsync(CategoryId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(AssetCategory.Create(TenantId, "computer", "Bilgisayar").Value));
        return categories;
    }

    private static Asset AssignedAsset(Guid? employeeId = null)
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", "SN-1", null).Value;
        asset.Assign(employeeId ?? EmployeeId, Issued);
        return asset;
    }

    private static ICurrentUser User(Guid? employeeId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.EmployeeId.Returns(employeeId);
        return user;
    }

    // ---- create / update: demirbaş no ----

    [Fact]
    public async Task CreateAssetHandler_WithFreeTag_StoresIt()
    {
        var assets = Substitute.For<IAssetRepository>();
        assets.IsAssetTagTakenAsync("DMB-1", null, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CreateAssetHandler(TenantContext(), assets, KnownCategory());

        var result = await handler.Handle(
            new CreateAssetCommand(CategoryId, "MacBook", null, null, AssetTag: " DMB-1 ", Brand: "Apple", PurchaseCost: 10m), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        assets.Received(1).Add(Arg.Is<Asset>(a => a.AssetTag == "DMB-1" && a.Brand == "Apple" && a.PurchaseCost == 10m));
    }

    [Fact]
    public async Task CreateAssetHandler_WithTakenTag_ConflictsAndAddsNothing()
    {
        var assets = Substitute.For<IAssetRepository>();
        assets.IsAssetTagTakenAsync("DMB-1", null, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateAssetHandler(TenantContext(), assets, KnownCategory());

        var result = await handler.Handle(new CreateAssetCommand(CategoryId, "MacBook", null, null, AssetTag: "DMB-1"), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.TagInUse);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Fact]
    public async Task CreateAssetHandler_WithoutTag_NeverChecksUniqueness()
    {
        var assets = Substitute.For<IAssetRepository>();
        var handler = new CreateAssetHandler(TenantContext(), assets, KnownCategory());

        (await handler.Handle(new CreateAssetCommand(CategoryId, "MacBook", null, null, AssetTag: "  "), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await assets.DidNotReceive().IsAssetTagTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAssetHandler_WithInvalidCost_FailsBeforeTouchingTheRepository()
    {
        var assets = Substitute.For<IAssetRepository>();
        var handler = new CreateAssetHandler(TenantContext(), assets, KnownCategory());

        var result = await handler.Handle(new CreateAssetCommand(CategoryId, "MacBook", null, null, AssetTag: "X", PurchaseCost: -1m), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.CostInvalid);
        await assets.DidNotReceive().IsAssetTagTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAssetHandler_ExcludesItsOwnRecordFromTheTagCheck()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook", null, null, new AssetDetails("DMB-1", null, null, null, null, null)).Value;
        var assets = RepositoryHolding(asset);
        assets.IsAssetTagTakenAsync("DMB-1", asset.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new UpdateAssetHandler(assets);

        var result = await handler.Handle(new UpdateAssetCommand(asset.Id, "MacBook 2", null, null, AssetTag: "DMB-1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await assets.Received(1).IsAssetTagTakenAsync("DMB-1", asset.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAssetHandler_WithTagOfAnotherAsset_Conflicts()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook", null, null).Value;
        var assets = RepositoryHolding(asset);
        assets.IsAssetTagTakenAsync("DMB-2", asset.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new UpdateAssetHandler(assets);

        var result = await handler.Handle(new UpdateAssetCommand(asset.Id, "MacBook", null, null, AssetTag: "DMB-2"), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.TagInUse);
    }

    // ---- return / lost ----

    [Fact]
    public async Task ReturnAssetHandler_PassesConditionAndNoteToTheLedger()
    {
        var asset = AssignedAsset();
        var handler = new ReturnAssetHandler(RepositoryHolding(asset), Substitute.For<IIntegrationEventOutbox>());

        var result = await handler.Handle(new ReturnAssetCommand(asset.Id, Issued.AddDays(5), AssetCondition.Damaged, "Çizik"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var row = asset.Assignments.Single();
        row.ReturnCondition.ShouldBe(AssetCondition.Damaged);
        row.Note.ShouldBe("Çizik");
        asset.Condition.ShouldBe(AssetCondition.Damaged);
    }

    [Fact]
    public async Task ReturnAssetHandler_WithTooLongNote_FailsAndEnqueuesNothing()
    {
        var asset = AssignedAsset();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new ReturnAssetHandler(RepositoryHolding(asset), outbox);

        var result = await handler.Handle(
            new ReturnAssetCommand(asset.Id, Issued, AssetCondition.Good, new string('a', AssetsLimits.NoteMaxLength + 1)), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.NoteTooLong);
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task MarkAssetLostHandler_ClosesTheAssignmentAsLost()
    {
        var asset = AssignedAsset();
        var handler = new MarkAssetLostHandler(RepositoryHolding(asset));

        var result = await handler.Handle(new MarkAssetLostCommand(asset.Id, Issued.AddDays(2), "kayıp"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Lost);
        asset.Assignments.Single().EndReason.ShouldBe(AssignmentEndReason.Lost);
    }

    [Fact]
    public async Task MarkAssetLostHandler_WhenRetired_FailsWithCannotLose()
    {
        var asset = Asset.Create(TenantId, CategoryId, "Eski", null, null).Value;
        asset.Retire();
        var handler = new MarkAssetLostHandler(RepositoryHolding(asset));

        var result = await handler.Handle(new MarkAssetLostCommand(asset.Id, Issued), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.CannotLose);
    }

    // ---- acknowledge ----

    [Fact]
    public async Task AcknowledgeAssetHandler_ByTheAssignee_StampsTheClockTime()
    {
        var asset = AssignedAsset();
        var now = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        var handler = new AcknowledgeAssetHandler(User(EmployeeId), RepositoryHolding(asset), new FixedTime(now));

        var result = await handler.Handle(new AcknowledgeAssetCommand(asset.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        asset.OpenAssignment!.AcknowledgedAt.ShouldBe(now.UtcDateTime);
    }

    [Fact]
    public async Task AcknowledgeAssetHandler_ByAnotherEmployee_IsForbidden()
    {
        var asset = AssignedAsset();
        var handler = new AcknowledgeAssetHandler(User(Guid.NewGuid()), RepositoryHolding(asset), TimeProvider.System);

        var result = await handler.Handle(new AcknowledgeAssetCommand(asset.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.NotAssignee);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        asset.OpenAssignment!.AcknowledgedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AcknowledgeAssetHandler_UserWithoutEmployeeRecord_IsForbidden()
    {
        var asset = AssignedAsset();
        var handler = new AcknowledgeAssetHandler(User(null), RepositoryHolding(asset), TimeProvider.System);

        var result = await handler.Handle(new AcknowledgeAssetCommand(asset.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.NotAssignee);
    }

    [Fact]
    public async Task AcknowledgeAssetHandler_SecondTime_FailsWithAlreadyAcknowledged()
    {
        var asset = AssignedAsset();
        var handler = new AcknowledgeAssetHandler(User(EmployeeId), RepositoryHolding(asset), TimeProvider.System);
        await handler.Handle(new AcknowledgeAssetCommand(asset.Id), CancellationToken.None);

        var result = await handler.Handle(new AcknowledgeAssetCommand(asset.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAcknowledged);
    }

    [Fact]
    public async Task AcknowledgeAssetHandler_UnknownAsset_IsNotFound()
    {
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<Asset>(Error.NotFound(AssetsDomainErrors.NotFound)));
        var handler = new AcknowledgeAssetHandler(User(EmployeeId), assets, TimeProvider.System);

        var result = await handler.Handle(new AcknowledgeAssetCommand(Guid.NewGuid()), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    // ---- validators ----

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void CreateAssetValidator_AssetTagLengthBoundary(int length, bool valid)
    {
        var command = new CreateAssetCommand(CategoryId, "A", null, null, AssetTag: new string('t', length));

        new CreateAssetValidator().Validate(command).IsValid.ShouldBe(valid);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void UpdateAssetValidator_BrandAndModelLengthBoundary(int length, bool valid)
    {
        var text = new string('b', length);

        new UpdateAssetValidator().Validate(new UpdateAssetCommand(Guid.NewGuid(), "A", null, null, Brand: text)).IsValid.ShouldBe(valid);
        new UpdateAssetValidator().Validate(new UpdateAssetCommand(Guid.NewGuid(), "A", null, null, Model: text)).IsValid.ShouldBe(valid);
    }

    [Theory]
    [InlineData("9999999999999999.99", true)]
    [InlineData("10000000000000000.00", false)]
    [InlineData("1.005", false)]
    [InlineData("0", true)]
    public void CreateAssetValidator_PurchaseCostMustFitDecimal18x2(string cost, bool valid)
    {
        var command = new CreateAssetCommand(CategoryId, "A", null, null, PurchaseCost: decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture));

        new CreateAssetValidator().Validate(command).IsValid.ShouldBe(valid);
    }

    [Fact]
    public void ReturnAssetValidator_RejectsAnUndefinedCondition()
    {
        new ReturnAssetValidator().Validate(new ReturnAssetCommand(Guid.NewGuid(), Issued, (AssetCondition)9)).IsValid.ShouldBeFalse();
        new ReturnAssetValidator().Validate(new ReturnAssetCommand(Guid.NewGuid(), Issued, AssetCondition.Fair)).IsValid.ShouldBeTrue();
    }

    // ---- DTO mapping / queries ----

    [Fact]
    public async Task GetMyAssetHistoryHandler_WithoutEmployeeRecord_IsNotFound()
    {
        var handler = new GetMyAssetHistoryHandler(User(null), Substitute.For<IAssetRepository>());

        var result = await handler.Handle(new GetMyAssetHistoryQuery(), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsErrors.NoEmployeeForUser);
    }

    [Fact]
    public async Task GetAssetHistoryHandler_UnknownAsset_IsNotFoundAndReadsNoLedger()
    {
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<Asset>(Error.NotFound(AssetsDomainErrors.NotFound)));
        var handler = new GetAssetHistoryHandler(assets);

        var result = await handler.Handle(new GetAssetHistoryQuery(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.NotFound);
        await assets.DidNotReceive().ListAssignmentsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssetDto_CarriesKunyeAndAcknowledgedAtOnlyWhileAssigned()
    {
        var asset = Asset.Create(
            TenantId, CategoryId, "MacBook", "SN-1", null,
            new AssetDetails("DMB-1", "Apple", "M3", new DateOnly(2026, 1, 1), 100m, new DateOnly(2027, 1, 1))).Value;
        asset.Assign(EmployeeId, Issued);
        var at = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
        asset.Acknowledge(EmployeeId, at);
        var assets = Substitute.For<IAssetRepository>();
        assets.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(new List<Asset> { asset });
        var handler = new GetMyAssetsHandler(User(EmployeeId), assets, Substitute.For<IAssetCategoryRepository>());

        var assigned = (await handler.Handle(new GetMyAssetsQuery(), CancellationToken.None)).Value.Single();

        assigned.AcknowledgedAt.ShouldBe(at);
        assigned.AssetTag.ShouldBe("DMB-1");
        assigned.Brand.ShouldBe("Apple");
        assigned.Model.ShouldBe("M3");
        assigned.PurchaseCost.ShouldBe(100m);
        assigned.Condition.ShouldBe(AssetCondition.Good);

        asset.Return(Issued.AddDays(1));
        (await handler.Handle(new GetMyAssetsQuery(), CancellationToken.None)).Value.Single().AcknowledgedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AssetSummaryHandler_CountsStatusesWarrantyAcknowledgementAndKkdOverdue()
    {
        var today = new DateOnly(2026, 10, 7);
        var ppe = AssetCategory.Create(TenantId, "ppe", "KKD", CategoryId, replacementIntervalDays: 30).Value;
        var otherCategory = Guid.NewGuid();
        IReadOnlyList<AssetSummaryRow> rows =
        [
            new(CategoryId, AssetStatus.Assigned, new DateOnly(2026, 9, 7), null, false),   // due 2026-10-07 -> not overdue (boundary)
            new(CategoryId, AssetStatus.Assigned, new DateOnly(2026, 9, 6), null, true),    // due 2026-10-06 -> overdue, acknowledged
            new(CategoryId, AssetStatus.Unassigned, null, today.AddDays(60), false),        // warranty expiring (window boundary, inclusive)
            new(CategoryId, AssetStatus.Unassigned, null, today.AddDays(61), false),        // outside window
            new(otherCategory, AssetStatus.Unassigned, null, today, false),                 // expiring today
            new(otherCategory, AssetStatus.Unassigned, null, today.AddDays(-1), false),     // expired
            new(otherCategory, AssetStatus.Retired, null, today.AddDays(-30), false),       // retired: excluded from warranty
            new(otherCategory, AssetStatus.Lost, null, today.AddDays(10), false),           // lost: excluded from warranty
            new(otherCategory, AssetStatus.PendingAssignment, null, null, false),
        ];
        var assets = Substitute.For<IAssetRepository>();
        assets.ListSummaryRowsAsync(Arg.Any<CancellationToken>()).Returns(rows);
        var categories = Substitute.For<IAssetCategoryRepository>();
        categories.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AssetCategory> { ppe });
        var handler = new AssetSummaryHandler(assets, categories, new FixedTime(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)));

        var summary = (await handler.Handle(new GetAssetSummaryQuery(), CancellationToken.None)).Value;

        summary.Total.ShouldBe(9);
        summary.Assigned.ShouldBe(2);
        summary.Unassigned.ShouldBe(4);
        summary.PendingAssignment.ShouldBe(1);
        summary.Retired.ShouldBe(1);
        summary.Lost.ShouldBe(1);
        summary.Unacknowledged.ShouldBe(1);
        summary.WarrantyExpiring.ShouldBe(2);
        summary.WarrantyExpired.ShouldBe(1);
        summary.ReplacementOverdue.ShouldBe(1);
        summary.Categories.Single(c => c.CategoryId == CategoryId).ShouldBe(new AssetCategorySummaryDto(CategoryId, 4, 2));
        summary.Categories.Single(c => c.CategoryId == otherCategory).ShouldBe(new AssetCategorySummaryDto(otherCategory, 5, 0));
    }
}
