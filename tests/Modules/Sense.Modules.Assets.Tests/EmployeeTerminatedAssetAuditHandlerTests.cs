using NSubstitute;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>
/// EmployeeTerminated (Employee module) -> faz4 offboarding↔zimmet: one AssetUnreturnedAtTermination event per
/// still-open asset, termination itself is never blocked (this handler doesn't touch Employee.Terminate()).
/// </summary>
public sealed class EmployeeTerminatedAssetAuditHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public async Task Handle_EmployeeWithOpenAssets_EnqueuesOneEventPerAsset_AndSaves()
    {
        var laptop = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", "SN-1", null).Value;
        laptop.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var phone = Asset.Create(TenantId, CategoryId, "iPhone 15", "SN-2", null).Value;
        phone.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        var assets = Substitute.For<IAssetRepository>();
        assets.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(new List<Asset> { laptop, phone });
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IAssetsUnitOfWork>();

        var handler = new EmployeeTerminatedAssetAuditHandler(assets, outbox, unitOfWork);
        var integrationEvent = new EmployeeTerminated(TenantId, EmployeeId, TerminationType.Resignation, "resignation", null);

        await handler.Handle(integrationEvent, CancellationToken.None);

        outbox.Received(1).Enqueue(Arg.Is<AssetUnreturnedAtTermination>(e => e.AssetId == laptop.Id && e.EmployeeId == EmployeeId && e.AssetName == "MacBook Pro 14"));
        outbox.Received(1).Enqueue(Arg.Is<AssetUnreturnedAtTermination>(e => e.AssetId == phone.Id && e.EmployeeId == EmployeeId && e.AssetName == "iPhone 15"));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmployeeWithNoOpenAssets_EnqueuesNothing_ButStillSaves()
    {
        var assets = Substitute.For<IAssetRepository>();
        assets.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(new List<Asset>());
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IAssetsUnitOfWork>();

        var handler = new EmployeeTerminatedAssetAuditHandler(assets, outbox, unitOfWork);
        var integrationEvent = new EmployeeTerminated(TenantId, EmployeeId, TerminationType.Resignation, "resignation", null);

        await handler.Handle(integrationEvent, CancellationToken.None);

        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
