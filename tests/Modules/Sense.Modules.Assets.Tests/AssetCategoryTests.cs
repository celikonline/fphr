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

        seed.Select(r => r.Code).ShouldBe(["computer", "phone", "vehicle", "other"]);
        seed.Select(r => r.Name).ShouldBe(["Bilgisayar", "Telefon", "Araç", "Diğer"]);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task EnsureSeededAsync_WhenNoCategoriesExist_AddsAllDefaults()
    {
        var repository = Substitute.For<IAssetCategoryRepository>();
        repository.AnyAsync(Arg.Any<CancellationToken>()).Returns(false);
        var added = new List<AssetCategory>();
        repository.When(r => r.Add(Arg.Any<AssetCategory>())).Do(call => added.Add(call.Arg<AssetCategory>()));
        var service = new AssetCategoryProvisioningService(repository);

        await service.EnsureSeededAsync(TenantId, CancellationToken.None);

        added.Count.ShouldBe(4);
        added.Select(c => c.Code).ShouldBe(["computer", "phone", "vehicle", "other"]);
    }

    [Fact]
    public async Task EnsureSeededAsync_WhenCategoriesAlreadyExist_AddsNothing()
    {
        var repository = Substitute.For<IAssetCategoryRepository>();
        repository.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        var service = new AssetCategoryProvisioningService(repository);

        await service.EnsureSeededAsync(TenantId, CancellationToken.None);

        repository.DidNotReceive().Add(Arg.Any<AssetCategory>());
    }
}
