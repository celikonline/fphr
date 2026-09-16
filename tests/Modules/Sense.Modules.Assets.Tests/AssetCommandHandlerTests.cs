using NSubstitute;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Application.Categories;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

public sealed class AssetCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static ITenantContext TenantContext()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    [Fact]
    public async Task CreateAssetHandler_WithValidCategory_AddsAsset()
    {
        var assets = Substitute.For<IAssetRepository>();
        var categories = Substitute.For<IAssetCategoryRepository>();
        categories.GetByIdAsync(CategoryId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(AssetCategory.Create(TenantId, "computer", "Bilgisayar").Value));
        var handler = new CreateAssetHandler(TenantContext(), assets, categories);

        var result = await handler.Handle(new CreateAssetCommand(CategoryId, "MacBook Pro 14", "SN-1", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        assets.Received(1).Add(Arg.Is<Asset>(a => a.Name == "MacBook Pro 14" && a.CategoryId == CategoryId));
    }

    [Fact]
    public async Task CreateAssetHandler_WithUnknownCategory_Fails()
    {
        var assets = Substitute.For<IAssetRepository>();
        var categories = Substitute.For<IAssetCategoryRepository>();
        categories.GetByIdAsync(CategoryId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AssetCategory>(Error.NotFound(AssetsDomainErrors.CategoryNotFound)));
        var handler = new CreateAssetHandler(TenantContext(), assets, categories);

        var result = await handler.Handle(new CreateAssetCommand(CategoryId, "MacBook Pro 14", null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.CategoryNotFound);
        assets.DidNotReceive().Add(Arg.Any<Asset>());
    }

    [Fact]
    public async Task AssignAssetHandler_WhenUnassigned_EnqueuesAssetAssigned()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new AssignAssetHandler(assets, outbox);
        var issuedAt = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, EmployeeId, issuedAt), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Assigned);
        outbox.Received(1).Enqueue(Arg.Is<AssetAssigned>(e => e.AssetId == asset.Id && e.EmployeeId == EmployeeId && e.IssuedAt == issuedAt));
    }

    [Fact]
    public async Task AssignAssetHandler_WhenAlreadyAssigned_FailsAndDoesNotEnqueue()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new AssignAssetHandler(assets, outbox);

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAssigned);
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task ReturnAssetHandler_WhenAssigned_EnqueuesAssetReturned()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new ReturnAssetHandler(assets, outbox);
        var returnedAt = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await handler.Handle(new ReturnAssetCommand(asset.Id, returnedAt), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Unassigned);
        outbox.Received(1).Enqueue(Arg.Is<AssetReturned>(e => e.AssetId == asset.Id && e.EmployeeId == EmployeeId && e.ReturnedAt == returnedAt));
    }

    [Fact]
    public async Task ReturnAssetHandler_WhenNotAssigned_FailsAndDoesNotEnqueue()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new ReturnAssetHandler(assets, outbox);

        var result = await handler.Handle(new ReturnAssetCommand(asset.Id, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.NotAssigned);
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task RetireAssetHandler_WhileAssigned_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var handler = new RetireAssetHandler(assets);

        var result = await handler.Handle(new RetireAssetCommand(asset.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.CannotRetireWhileAssigned);
    }

    [Fact]
    public async Task RetireAssetHandler_WhenUnassigned_Succeeds()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var handler = new RetireAssetHandler(assets);

        var result = await handler.Handle(new RetireAssetCommand(asset.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Retired);
    }

    [Fact]
    public async Task GetMyAssetsHandler_WithoutEmployeeLinkedToUser_Fails()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);
        var assets = Substitute.For<IAssetRepository>();
        var handler = new GetMyAssetsHandler(currentUser, assets);

        var result = await handler.Handle(new GetMyAssetsQuery(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsErrors.NoEmployeeForUser);
    }

    [Fact]
    public async Task GetMyAssetsHandler_WithEmployee_ReturnsMappedDtos()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(EmployeeId);
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var assets = Substitute.For<IAssetRepository>();
        assets.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(new List<Asset> { asset });
        var handler = new GetMyAssetsHandler(currentUser, assets);

        var result = await handler.Handle(new GetMyAssetsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value[0].Id.ShouldBe(asset.Id);
        result.Value[0].Status.ShouldBe(AssetStatus.Assigned);
    }
}
