using NSubstitute;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Import;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

public sealed class AssetImportRowHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private const string NationalId = "10000000146";

    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IAssetCategoryRepository _categories = Substitute.For<IAssetCategoryRepository>();
    private readonly IAssetRepository _assets = Substitute.For<IAssetRepository>();
    private readonly IAssetsUnitOfWork _unitOfWork = Substitute.For<IAssetsUnitOfWork>();

    public AssetImportRowHandlerTests()
    {
        _tenant.TenantId.Returns(TenantId);
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[]);
        _employeeLookup.FindActiveByNationalIdAsync(TenantId, NationalId, Arg.Any<CancellationToken>()).Returns(EmployeeId);
    }

    private AssetImportRowHandler CreateHandler() => new(_tenant, _employeeLookup, _categories, _assets, _unitOfWork);

    private static ImportRow Row(
        int rowNumber, string? nationalId, string? categoryName, string? serialNumber, string? assignedAt, string? returnedAt = null, string? note = null, string? name = null)
    {
        var values = new Dictionary<string, ImportCell>();
        void Set(string key, string? value)
        {
            if (value is not null)
            {
                values[key] = new ImportCell(value);
            }
        }

        Set(AssetImportColumns.NationalId, nationalId);
        Set(AssetImportColumns.CategoryName, categoryName);
        Set(AssetImportColumns.SerialNumber, serialNumber);
        Set(AssetImportColumns.AssignedAt, assignedAt);
        Set(AssetImportColumns.ReturnedAt, returnedAt);
        Set(AssetImportColumns.Note, note);
        Set(AssetImportColumns.Name, name);
        return new ImportRow(rowNumber, values);
    }

    private static ImportBatch Batch(ImportFailurePolicy policy, params ImportRow[] rows) =>
        new(ImportMode.Upsert, policy, [AssetImportColumns.NationalId], rows);

    private static ImportBatch UpdateOnlyBatch(ImportFailurePolicy policy, params ImportRow[] rows) =>
        new(ImportMode.UpdateOnly, policy, [AssetImportColumns.NationalId], rows);

    [Fact]
    public async Task ProcessAsync_OpenRow_NewCategoryAndAsset_AssignsAndInserts()
    {
        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Bilgisayar", "SN-1", "01.09.2026"));

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _categories.Received(1).Add(Arg.Is<AssetCategory>(c => c.Name == "Bilgisayar" && c.TenantId == TenantId));
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.Status == AssetStatus.Assigned && a.AssignedEmployeeId == EmployeeId && a.SerialNumber == "SN-1"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ExistingCategoryName_ReusesIt_DoesNotCreateANewOne()
    {
        var existing = AssetCategory.Create(TenantId, "bilgisayar", "Bilgisayar").Value;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[existing]);

        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "bilgisayar", "SN-2", "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _categories.DidNotReceive().Add(Arg.Any<AssetCategory>());
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.CategoryId == existing.Id));
    }

    [Fact]
    public async Task ProcessAsync_MatchingCategoryAndSerial_UpdatesTheExistingAsset_DoesNotCreateANewOne()
    {
        var category = AssetCategory.Create(TenantId, "telefon", "Telefon").Value;
        var existing = Asset.Create(TenantId, category.Id, "iPhone 15", "SN-3", null).Value;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-3", Arg.Any<CancellationToken>()).Returns(existing);

        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Telefon", "SN-3", "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Updated);
        existing.Status.ShouldBe(AssetStatus.Assigned);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Fact]
    public async Task ProcessAsync_ClosedRow_NewAsset_AssignsThenReturns_EndsUnassignedWithReturnedAtSet()
    {
        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Araç", null, "01.03.2026", "15.08.2026"));

        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _assets.Received(1).Add(Arg.Is<Asset>(a =>
            a.Status == AssetStatus.Unassigned && a.AssignedEmployeeId == null && a.ReturnedAt == new DateOnly(2026, 8, 15)));
    }

    [Fact]
    public async Task ProcessAsync_ClosedRow_ExistingAssignedAsset_ReturnsDirectly()
    {
        var category = AssetCategory.Create(TenantId, "telefon", "Telefon").Value;
        var existing = Asset.Create(TenantId, category.Id, "iPhone 15", "SN-4", null).Value;
        existing.Assign(EmployeeId, new DateOnly(2026, 3, 1));
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-4", Arg.Any<CancellationToken>()).Returns(existing);

        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Telefon", "SN-4", "01.03.2026", "15.08.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Updated);
        existing.Status.ShouldBe(AssetStatus.Unassigned);
        existing.ReturnedAt.ShouldBe(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public async Task ProcessAsync_UnmatchedEmployee_ReportsNoMatch()
    {
        _employeeLookup.FindActiveByNationalIdAsync(TenantId, "00000000000", Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, "00000000000", "Bilgisayar", "SN-5", "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == ImportErrorCodes.NoMatch);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_MissingRequiredFields_ReportsRequiredMissing()
    {
        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, null, null, "SN-6", null));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.Count(e => e.Code == ImportErrorCodes.RequiredMissing).ShouldBe(1);
        result.Rows[0].Errors[0].Column.ShouldBe(AssetImportColumns.CategoryName);
    }

    [Fact]
    public async Task ProcessAsync_AllOrNothing_SkipsValidRowsWhenAnyRowFails()
    {
        _employeeLookup.FindActiveByNationalIdAsync(TenantId, "00000000000", Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var rows = new[]
        {
            Row(2, NationalId, "Bilgisayar", "SN-7", "01.09.2026"),
            Row(3, "00000000000", "Bilgisayar", "SN-8", "01.09.2026"),
        };
        var batch = Batch(ImportFailurePolicy.AllOrNothing, rows);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows.First(r => r.RowNumber == 2).Outcome.ShouldBe(ImportRowOutcome.Skipped);
        result.Rows.First(r => r.RowNumber == 3).Outcome.ShouldBe(ImportRowOutcome.Error);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_AssetAlreadyAssignedToSomeoneElse_ReportsDomainError()
    {
        var category = AssetCategory.Create(TenantId, "telefon", "Telefon").Value;
        var existing = Asset.Create(TenantId, category.Id, "iPhone 15", "SN-9", null).Value;
        existing.Assign(Guid.NewGuid(), new DateOnly(2026, 1, 1));
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-9", Arg.Any<CancellationToken>()).Returns(existing);

        var batch = Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Telefon", "SN-9", "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == AssetsDomainErrors.AlreadyAssigned);
    }

    // --- TK-003: "Sadece güncelleme" (UpdateOnly) mode ---

    [Fact]
    public async Task ProcessAsync_UpdateOnly_MatchingCategoryAndSerial_UpdatesExisting()
    {
        var category = AssetCategory.Create(TenantId, "telefon", "Telefon").Value;
        var existing = Asset.Create(TenantId, category.Id, "iPhone 15", "SN-10", null).Value;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-10", Arg.Any<CancellationToken>()).Returns(existing);

        var batch = UpdateOnlyBatch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Telefon", "SN-10", "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Updated);
        existing.Status.ShouldBe(AssetStatus.Assigned);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
        _categories.DidNotReceive().Add(Arg.Any<AssetCategory>());
    }

    [Fact]
    public async Task ProcessAsync_UpdateOnly_UnknownSerialNumberInExistingCategory_ReportsErrorAndCreatesNothing()
    {
        var category = AssetCategory.Create(TenantId, "telefon", "Telefon").Value;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-DOES-NOT-EXIST", Arg.Any<CancellationToken>()).Returns((Asset?)null);

        var batch = UpdateOnlyBatch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Telefon", "SN-DOES-NOT-EXIST", "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == ImportErrorCodes.LookupNotFound && e.Column == AssetImportColumns.SerialNumber);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
        _categories.DidNotReceive().Add(Arg.Any<AssetCategory>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_UpdateOnly_UnknownCategory_ReportsErrorAndDoesNotCreateTheCategory()
    {
        // No existing categories at all - "Bilgisayar" is unknown to this tenant.
        var batch = UpdateOnlyBatch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Bilgisayar", "SN-11", "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == ImportErrorCodes.LookupNotFound && e.Column == AssetImportColumns.CategoryName);
        _categories.DidNotReceive().Add(Arg.Any<AssetCategory>());
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
        await _assets.DidNotReceive().FindByCategoryAndSerialNumberAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_UpdateOnly_MissingSerialNumber_ReportsRequiredMissing_NoMatchKeyAvailable()
    {
        var batch = UpdateOnlyBatch(ImportFailurePolicy.SkipInvalidRows, Row(2, NationalId, "Bilgisayar", null, "01.09.2026"));
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Code == ImportErrorCodes.RequiredMissing && e.Column == AssetImportColumns.SerialNumber);
        _categories.DidNotReceive().Add(Arg.Any<AssetCategory>());
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Fact]
    public async Task ProcessAsync_UpdateOnly_AllOrNothing_NoMatchRowMakesEverythingSkippedOrErrored()
    {
        var category = AssetCategory.Create(TenantId, "telefon", "Telefon").Value;
        var existing = Asset.Create(TenantId, category.Id, "iPhone 15", "SN-12", null).Value;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-12", Arg.Any<CancellationToken>()).Returns(existing);

        var rows = new[]
        {
            Row(2, NationalId, "Telefon", "SN-12", "01.09.2026"),
            Row(3, NationalId, "Bilgisayar", null, "01.09.2026"), // missing serial number -> row-level error before plans are built
        };
        var batch = UpdateOnlyBatch(ImportFailurePolicy.AllOrNothing, rows);
        var result = await CreateHandler().ProcessAsync(batch, TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows.First(r => r.RowNumber == 2).Outcome.ShouldBe(ImportRowOutcome.Skipped);
        result.Rows.First(r => r.RowNumber == 3).Outcome.ShouldBe(ImportRowOutcome.Error);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Template_InventoryAndLegacyAssignmentColumnsRemainAvailable()
    {
        var template = new AssetImportTemplate();
        var columns = await template.GetColumnsAsync(TestContext.Current.CancellationToken);

        template.Code.ShouldBe("assets.zimmet");
        columns.Single(c => c.Key == AssetImportColumns.NationalId).Requirement.ShouldBe(ImportColumnRequirement.Optional);
        columns.Single(c => c.Key == AssetImportColumns.NationalId).IsMatchKey.ShouldBeFalse();
        columns.Single(c => c.Key == AssetImportColumns.AssignedAt).Requirement.ShouldBe(ImportColumnRequirement.Optional);
        columns.Single(c => c.Key == AssetImportColumns.Name).Requirement.ShouldBe(ImportColumnRequirement.Optional);
        columns.Single(c => c.Key == AssetImportColumns.CategoryName).Requirement.ShouldBe(ImportColumnRequirement.Always);
        template.GetSampleRows(columns).ShouldContain(r => !r.Values.ContainsKey(AssetImportColumns.NationalId) || string.IsNullOrWhiteSpace(r.Values[AssetImportColumns.NationalId]));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public async Task ProcessAsync_InventoryRow_CreatesUnassignedAssetWithoutLookingUpAnEmployee(string? employee, string? date)
    {
        var result = await CreateHandler().ProcessAsync(
            Batch(ImportFailurePolicy.SkipInvalidRows, Row(2, employee, "Bilgisayar", " SN-20 ", date, date, " Stok ", " Elite book ")),
            TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.Name == "Elite book" && a.SerialNumber == "SN-20" && a.Description == "Stok" &&
            a.Status == AssetStatus.Unassigned && a.AssignedEmployeeId == null && a.IssuedAt == null && a.ReturnedAt == null && a.Assignments.Count == 0));
        await _employeeLookup.DidNotReceive().FindActiveByNationalIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_MixedInventoryAndAssignmentRows_BothAreImported()
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.AllOrNothing,
            Row(2, null, "Bilgisayar", "SN-20", null), Row(3, NationalId, "Telefon", "SN-21", "01.09.2026")),
            TestContext.Current.CancellationToken);

        result.Applied.ShouldBeTrue();
        result.Rows.ShouldAllBe(r => r.Outcome == ImportRowOutcome.Inserted);
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.Status == AssetStatus.Unassigned && a.Name == "Bilgisayar – SN-20"));
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.Status == AssetStatus.Assigned && a.Assignments.Count == 1));
    }

    [Theory]
    [InlineData("01.09.2026", null)]
    [InlineData(null, "02.09.2026")]
    [InlineData("invalid", null)]
    public async Task ProcessAsync_DateWithoutEmployee_ReportsMissingEmployee(string? assigned, string? returned)
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, null, "Bilgisayar", "SN-20", assigned, returned)), TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Error);
        result.Rows[0].Errors.ShouldContain(e => e.Column == AssetImportColumns.NationalId && e.Code == ImportErrorCodes.RequiredMissing);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Fact]
    public async Task ProcessAsync_EmployeeWithoutIssueDate_ReportsMissingDate()
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, NationalId, "Bilgisayar", "SN-20", "   ")), TestContext.Current.CancellationToken);

        result.Rows[0].Errors.ShouldContain(e => e.Column == AssetImportColumns.AssignedAt && e.Code == ImportErrorCodes.RequiredMissing);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Fact]
    public async Task ProcessAsync_TooLongName_ReportsValueTooLongBeforeMutation()
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, null, "Bilgisayar", "SN-20", null, name: new string('a', AssetsLimits.NameMaxLength + 1))), TestContext.Current.CancellationToken);

        result.Rows[0].Errors.ShouldContain(e => e.Column == AssetImportColumns.Name && e.Code == ImportErrorCodes.ValueTooLong);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Theory]
    [InlineData(AssetStatus.Assigned)]
    [InlineData(AssetStatus.Unassigned)]
    [InlineData(AssetStatus.Retired)]
    [InlineData(AssetStatus.Lost)]
    [InlineData(AssetStatus.PendingAssignment)]
    public async Task ProcessAsync_MatchingInventoryRow_PreservesAssetStateAndLedger(AssetStatus status)
    {
        var category = AssetCategory.Create(TenantId, "bilgisayar", "Bilgisayar").Value;
        var details = new AssetDetails("TAG-20", "Brand", "Model", new DateOnly(2026, 1, 1), 1234m, new DateOnly(2027, 1, 1));
        var asset = Asset.Create(TenantId, category.Id, "Existing", "SN-20", "Details", details).Value;
        asset.Update(asset.Name, asset.SerialNumber, asset.Description, condition: AssetCondition.Damaged);
        if (status == AssetStatus.Assigned) asset.Assign(EmployeeId, new DateOnly(2026, 9, 1));
        if (status == AssetStatus.Retired) asset.Retire();
        if (status == AssetStatus.Lost) asset.MarkLost(new DateOnly(2026, 9, 1));
        if (status == AssetStatus.PendingAssignment) asset.RequestAssignment(EmployeeId, new DateOnly(2026, 9, 1));
        var assignmentCount = asset.Assignments.Count;
        var employee = asset.AssignedEmployeeId;
        var issued = asset.IssuedAt;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-20", Arg.Any<CancellationToken>()).Returns(asset);

        var result = await CreateHandler().ProcessAsync(UpdateOnlyBatch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, null, "Bilgisayar", "SN-20", null, note: "   ", name: "Updated name")), TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Updated);
        asset.Name.ShouldBe("Updated name");
        asset.Description.ShouldBe("Details");
        asset.Status.ShouldBe(status);
        asset.AssignedEmployeeId.ShouldBe(employee);
        asset.IssuedAt.ShouldBe(issued);
        asset.Assignments.Count.ShouldBe(assignmentCount);
        asset.AssetTag.ShouldBe(details.AssetTag);
        asset.Brand.ShouldBe(details.Brand);
        asset.Model.ShouldBe(details.Model);
        asset.PurchaseDate.ShouldBe(details.PurchaseDate);
        asset.PurchaseCost.ShouldBe(details.PurchaseCost);
        asset.WarrantyEndsOn.ShouldBe(details.WarrantyEndsOn);
        asset.Condition.ShouldBe(AssetCondition.Damaged);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Theory]
    [InlineData("SN-20")]
    [InlineData("sn-20")]
    public async Task ProcessAsync_DuplicateCategoryAndSerialInBatch_CreatesOnePhysicalAsset(string secondSerial)
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, null, "Bilgisayar", " SN-20 ", null), Row(3, NationalId, "bilgisayar", secondSerial, "01.09.2026")), TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        result.Rows[1].Outcome.ShouldBe(ImportRowOutcome.Updated);
        result.Rows[0].TargetEntityId.ShouldBe(result.Rows[1].TargetEntityId);
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.Status == AssetStatus.Assigned));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessAsync_UpdateOnly_BlankSerial_ReportsMissingMatchKey(string serial)
    {
        var result = await CreateHandler().ProcessAsync(UpdateOnlyBatch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, null, "Bilgisayar", serial, null)), TestContext.Current.CancellationToken);

        result.Rows[0].Errors.ShouldContain(e => e.Column == AssetImportColumns.SerialNumber && e.Code == ImportErrorCodes.RequiredMissing);
    }

    [Fact]
    public async Task ProcessAsync_InvalidClosedRow_DoesNotPersistAnOrphanAssetAlongsideValidRows()
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, NationalId, "Bilgisayar", "BAD", "02.09.2026", "01.09.2026"),
            Row(3, NationalId, "Telefon", "GOOD", "01.09.2026")), TestContext.Current.CancellationToken);

        result.Rows[0].Errors.ShouldContain(e => e.Code == AssetsDomainErrors.ReturnBeforeIssue);
        _assets.DidNotReceive().Add(Arg.Is<Asset>(a => a.SerialNumber == "BAD"));
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.SerialNumber == "GOOD"));
    }

    [Fact]
    public async Task ProcessAsync_InvalidClosedRow_DoesNotMutateExistingUnassignedAsset()
    {
        var category = AssetCategory.Create(TenantId, "bilgisayar", "Bilgisayar").Value;
        var asset = Asset.Create(TenantId, category.Id, "Existing", "SN-20", null).Value;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-20", Arg.Any<CancellationToken>()).Returns(asset);

        await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, NationalId, "Bilgisayar", "SN-20", "02.09.2026", "01.09.2026"),
            Row(3, null, "Telefon", "GOOD", null)), TestContext.Current.CancellationToken);

        asset.Status.ShouldBe(AssetStatus.Unassigned);
        asset.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_AllOrNothing_LateDomainFailure_DoesNotSaveAndSkipsSuccessfulRows()
    {
        var category = AssetCategory.Create(TenantId, "bilgisayar", "Bilgisayar").Value;
        var assigned = Asset.Create(TenantId, category.Id, "Existing", "TAKEN", null).Value;
        assigned.Assign(EmployeeId, new DateOnly(2026, 9, 1));
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "TAKEN", Arg.Any<CancellationToken>()).Returns(assigned);

        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.AllOrNothing,
            Row(2, NationalId, "Telefon", "NEW", "01.09.2026"),
            Row(3, NationalId, "Bilgisayar", "TAKEN", "01.09.2026")), TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Skipped);
        result.Rows[0].TargetEntityId.ShouldBeNull();
        result.Rows[1].Errors.ShouldContain(e => e.Code == AssetsDomainErrors.AlreadyAssigned);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_AllOrNothing_LateLookupFailure_DoesNotSave()
    {
        var category = AssetCategory.Create(TenantId, "bilgisayar", "Bilgisayar").Value;
        var asset = Asset.Create(TenantId, category.Id, "Existing", "SN-20", null).Value;
        _categories.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<AssetCategory>)[category]);
        _assets.FindByCategoryAndSerialNumberAsync(category.Id, "SN-20", Arg.Any<CancellationToken>()).Returns(asset);

        var result = await CreateHandler().ProcessAsync(UpdateOnlyBatch(ImportFailurePolicy.AllOrNothing,
            Row(2, NationalId, "Bilgisayar", "SN-20", "01.09.2026"),
            Row(3, null, "Unknown", "MISSING", null)), TestContext.Current.CancellationToken);

        result.Applied.ShouldBeFalse();
        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Skipped);
        result.Rows[1].Errors.ShouldContain(e => e.Code == ImportErrorCodes.LookupNotFound);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessAsync_InventoryWithoutSerial_CreatesDistinctItemsWithCategoryNameFallback(string? serial)
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, null, "Bilgisayar", serial, null), Row(3, null, "Bilgisayar", serial, null)), TestContext.Current.CancellationToken);

        result.Rows.ShouldAllBe(r => r.Outcome == ImportRowOutcome.Inserted);
        result.Rows[0].TargetEntityId.ShouldNotBe(result.Rows[1].TargetEntityId);
        _assets.Received(2).Add(Arg.Is<Asset>(a => a.SerialNumber == null && a.Name == "Bilgisayar"));
        await _assets.DidNotReceive().FindByCategoryAndSerialNumberAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AssetImportColumns.CategoryName, AssetsLimits.CategoryNameMaxLength)]
    [InlineData(AssetImportColumns.SerialNumber, AssetsLimits.SerialNumberMaxLength)]
    [InlineData(AssetImportColumns.Note, AssetsLimits.DescriptionMaxLength)]
    public async Task ProcessAsync_InventoryWithTooLongField_ReportsColumnError(string column, int limit)
    {
        var cells = new Dictionary<string, ImportCell>
        {
            [AssetImportColumns.CategoryName] = new("Bilgisayar"),
            [column] = new(new string('a', limit + 1)),
        };

        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows, new ImportRow(2, cells)), TestContext.Current.CancellationToken);

        result.Rows[0].Errors.ShouldContain(e => e.Column == column && e.Code == ImportErrorCodes.ValueTooLong);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
        _categories.DidNotReceive().Add(Arg.Any<AssetCategory>());
    }

    [Fact]
    public async Task ProcessAsync_AssignedAssetNameAtLimit_PreservesExplicitNameAndLedger()
    {
        var name = new string('a', AssetsLimits.NameMaxLength);
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, NationalId, "Bilgisayar", "SN-20", "01.09.2026", name: name)), TestContext.Current.CancellationToken);

        result.Rows[0].Outcome.ShouldBe(ImportRowOutcome.Inserted);
        _assets.Received(1).Add(Arg.Is<Asset>(a => a.Name == name && a.Status == AssetStatus.Assigned && a.Assignments.Count == 1));
    }

    [Fact]
    public async Task ProcessAsync_AssignmentWithInvalidDate_ReportsColumnError()
    {
        var result = await CreateHandler().ProcessAsync(Batch(ImportFailurePolicy.SkipInvalidRows,
            Row(2, NationalId, "Bilgisayar", "SN-20", "invalid")), TestContext.Current.CancellationToken);

        result.Rows[0].Errors.ShouldContain(e => e.Column == AssetImportColumns.AssignedAt && e.Code == ImportErrorCodes.InvalidDate);
        _assets.DidNotReceive().Add(Arg.Any<Asset>());
    }
}
