using NSubstitute;
using Sense.Modules.Assets.Application.Categories;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

public sealed class AssetCategoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyCode_Fails()
    {
        var result = AssetCategory.Create(TenantId, " ", "Bilgisayar");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.CategoryCodeRequired);
    }

    [Fact]
    public void Create_WithEmptyName_Fails()
    {
        var result = AssetCategory.Create(TenantId, "computer", " ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.NameRequired);
    }

    [Fact]
    public void Rename_UpdatesName()
    {
        var category = AssetCategory.Create(TenantId, "computer", "Bilgisayar").Value;

        var result = category.Rename("Dizüstü Bilgisayar");

        result.IsSuccess.ShouldBeTrue();
        category.Name.ShouldBe("Dizüstü Bilgisayar");
    }

    [Fact]
    public async Task SeedRows_MatchSpecifiedDefaults()
    {
        var seed = AssetCategorySeed.SystemDefinitions();

        seed.Select(r => r.Code).ShouldBe(["computer", "phone", "vehicle", "other", "ppe"]);
        seed.Select(r => r.Name).ShouldBe(["Bilgisayar", "Telefon", "Araç", "Diğer", "Kişisel koruyucu donanım (KKD)"]);
        seed.Single(r => r.Code == "ppe").ReplacementIntervalDays.ShouldBe(365);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task EnsureSeededAsync_WhenNoCategoriesExist_AddsAllDefaults()
    {
        var repository = Substitute.For<IAssetCategoryRepository>();
        repository.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AssetCategory>());
        var added = new List<AssetCategory>();
        repository.When(r => r.Add(Arg.Any<AssetCategory>())).Do(call => added.Add(call.Arg<AssetCategory>()));
        var service = new AssetCategoryProvisioningService(repository);

        await service.EnsureSeededAsync(TenantId, CancellationToken.None);

        added.Count.ShouldBe(5);
        added.Select(c => c.Code).ShouldBe(["computer", "phone", "vehicle", "other", "ppe"]);
        added.Single(c => c.Code == "ppe").RequiresPeriodicReplacement.ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureSeededAsync_WhenAllSeedCodesExist_AddsNothing()
    {
        var repository = Substitute.For<IAssetCategoryRepository>();
        repository.ListAsync(Arg.Any<CancellationToken>()).Returns(
            AssetCategorySeed.SystemDefinitions().Select(r => AssetCategory.Create(TenantId, r.Code, r.Name).Value).ToList());
        var service = new AssetCategoryProvisioningService(repository);

        await service.EnsureSeededAsync(TenantId, CancellationToken.None);

        repository.DidNotReceive().Add(Arg.Any<AssetCategory>());
    }

    [Fact]
    public async Task EnsureSeededAsync_ForTenantSeededBeforePpe_BackfillsOnlyPpe()
    {
        var repository = Substitute.For<IAssetCategoryRepository>();
        repository.ListAsync(Arg.Any<CancellationToken>()).Returns(
            new[] { "computer", "phone", "vehicle", "other" }.Select(c => AssetCategory.Create(TenantId, c, c).Value).ToList());
        var added = new List<AssetCategory>();
        repository.When(r => r.Add(Arg.Any<AssetCategory>())).Do(call => added.Add(call.Arg<AssetCategory>()));
        var service = new AssetCategoryProvisioningService(repository);

        await service.EnsureSeededAsync(TenantId, CancellationToken.None);

        added.Select(c => c.Code).ShouldBe(["ppe"]);
        added[0].ReplacementIntervalDays.ShouldBe(365);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public void ReplacementInterval_OutOfRange_Fails(int days)
    {
        var category = AssetCategory.Create(TenantId, "ppe", "KKD").Value;

        category.SetReplacementInterval(days).Error.Code.ShouldBe(AssetsDomainErrors.CategoryReplacementIntervalInvalid);
        AssetCategory.Create(TenantId, "ppe", "KKD", replacementIntervalDays: days).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ReplacementDueOn_IsIssueDatePlusInterval()
    {
        var category = AssetCategory.Create(TenantId, "ppe", "KKD", replacementIntervalDays: 180).Value;

        category.ReplacementDueOn(new DateOnly(2026, 1, 10)).ShouldBe(new DateOnly(2026, 7, 9));
        category.ReplacementDueOn(null).ShouldBeNull();
        category.SetReplacementInterval(null).IsSuccess.ShouldBeTrue();
        category.ReplacementDueOn(new DateOnly(2026, 1, 10)).ShouldBeNull();
    }
}
