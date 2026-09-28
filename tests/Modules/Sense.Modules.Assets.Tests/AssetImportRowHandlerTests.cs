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
        int rowNumber, string? nationalId, string? categoryName, string? serialNumber, string? assignedAt, string? returnedAt = null, string? note = null)
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
        result.Rows[0].Errors.Count(e => e.Code == ImportErrorCodes.RequiredMissing).ShouldBe(3);
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
}
