using NSubstitute;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>QA teslim testi bulgu 13: unknown employee, hire/return date order, serial-number uniqueness among assigned
/// assets and idempotent termination audit events.</summary>
public sealed class AssetIntegrityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static (AssignAssetHandler Handler, Asset Asset, IAssetRepository Assets, IIntegrationEventOutbox Outbox) Build(
        EmployeeEmploymentDates? employment, string? serialNumber = "SN-1")
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", serialNumber, null).Value;
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var lookup = Substitute.For<IEmployeeSalaryLookup>();
        lookup.GetEmploymentDatesAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(employment));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        return (new AssignAssetHandler(tenant, assets, outbox, lookup), asset, assets, outbox);
    }

    [Fact]
    public async Task Assign_ToAnEmployeeThatDoesNotExist_IsNotFound_AndRaisesNoEvent()
    {
        var (handler, asset, _, outbox) = Build(employment: null);

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, EmployeeId, Today), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsErrors.EmployeeNotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        asset.Status.ShouldBe(AssetStatus.Unassigned);
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task Assign_WithAnIssueDateBeforeTheHireDate_IsRejected()
    {
        var (handler, asset, _, outbox) = Build(new EmployeeEmploymentDates(Today, null));

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, EmployeeId, Today.AddDays(-10)), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsErrors.IssuedBeforeHire);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task Assign_WhenTheSameSerialNumberIsAlreadyAssignedElsewhere_IsAConflict()
    {
        var (handler, asset, assets, outbox) = Build(new EmployeeEmploymentDates(Today.AddYears(-1), null));
        assets.IsSerialNumberAssignedElsewhereAsync("SN-1", asset.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, EmployeeId, Today), CancellationToken.None);

        result.Error.Code.ShouldBe(AssetsDomainErrors.SerialNumberInUse);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        asset.Status.ShouldBe(AssetStatus.Unassigned);
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task Assign_ForAValidEmployeeAndSerial_Succeeds()
    {
        var (handler, asset, _, outbox) = Build(new EmployeeEmploymentDates(Today.AddYears(-1), null));

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, EmployeeId, Today), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        outbox.Received(1).Enqueue(Arg.Any<AssetAssigned>());
    }

    [Fact]
    public void Return_BeforeTheIssueDate_IsRejected_AndKeepsTheAssignment()
    {
        var asset = Asset.Create(TenantId, CategoryId, "Telefon", null, null).Value;
        asset.Assign(EmployeeId, Today);

        var result = asset.Return(Today.AddDays(-3));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.ReturnBeforeIssue);
        asset.Status.ShouldBe(AssetStatus.Assigned);
    }

    [Fact]
    public async Task TerminationAudit_WhenAlreadyWrittenForTheAsset_DoesNotWriteItAgain()
    {
        var asset = Asset.Create(TenantId, CategoryId, "Laptop", "SN-9", null).Value;
        asset.Assign(EmployeeId, Today);
        var assets = Substitute.For<IAssetRepository>();
        assets.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(new List<Asset> { asset });
        assets.HasUnreturnedTerminationAuditAsync(TenantId, asset.Id, EmployeeId, Arg.Any<CancellationToken>()).Returns(true);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new EmployeeTerminatedAssetAuditHandler(assets, outbox, Substitute.For<IAssetsUnitOfWork>());

        await handler.Handle(new EmployeeTerminated(TenantId, EmployeeId, TerminationType.Resignation, "resignation", null), CancellationToken.None);

        outbox.DidNotReceive().Enqueue(Arg.Any<AssetUnreturnedAtTermination>());
    }
}
