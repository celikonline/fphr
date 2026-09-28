using NSubstitute;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Application.Categories;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
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

    private static IEmployeeSalaryLookup EmployeeDates(DateOnly? hireDate)
    {
        var lookup = Substitute.For<IEmployeeSalaryLookup>();
        lookup.GetEmploymentDatesAsync(TenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeEmploymentDates?>(new EmployeeEmploymentDates(hireDate, null)));
        return lookup;
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

    private static IWorkflowService WorkflowService(Result<Guid> approvalResult)
    {
        var workflow = Substitute.For<IWorkflowService>();
        workflow.StartAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ApprovalStepInput>?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(approvalResult));
        return workflow;
    }

    [Fact]
    public async Task AssignAssetHandler_WhenUnassigned_StartsWorkflowApprovalAndMovesToPending()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var approvalRequestId = Guid.NewGuid();
        var workflow = WorkflowService(Result.Success(approvalRequestId));
        var handler = new AssignAssetHandler(TenantContext(), assets, EmployeeDates(null), workflow);
        var issuedAt = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, EmployeeId, issuedAt), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.PendingAssignment);
        asset.PendingEmployeeId.ShouldBe(EmployeeId);
        asset.ApprovalRequestId.ShouldBe(approvalRequestId);
        await workflow.Received(1).StartAsync(ApprovalRequestTypes.AssetAssignment, asset.Id, EmployeeId, null, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignAssetHandler_WhenAlreadyAssigned_FailsAndDoesNotStartWorkflow()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var assets = Substitute.For<IAssetRepository>();
        assets.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(asset));
        var workflow = WorkflowService(Result.Success(Guid.NewGuid()));
        var handler = new AssignAssetHandler(TenantContext(), assets, EmployeeDates(null), workflow);

        var result = await handler.Handle(new AssignAssetCommand(asset.Id, Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAssigned);
        await workflow.DidNotReceive().StartAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ApprovalStepInput>?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssetAssignmentApprovalCompletedHandler_ForPendingAssignment_CompletesAndEnqueuesAssetAssigned()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var issuedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        asset.RequestAssignment(EmployeeId, issuedAt);
        var approvalRequestId = Guid.NewGuid();
        asset.AttachApproval(approvalRequestId);
        var assets = Substitute.For<IAssetRepository>();
        assets.FindByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Asset?>(asset));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IAssetsUnitOfWork>();
        var handler = new AssetAssignmentApprovalCompletedHandler(assets, outbox, unitOfWork, TimeProvider.System);

        await handler.Handle(new ApprovalCompleted(TenantId, approvalRequestId, ApprovalRequestTypes.AssetAssignment, asset.Id, EmployeeId), CancellationToken.None);

        asset.Status.ShouldBe(AssetStatus.Assigned);
        asset.AssignedEmployeeId.ShouldBe(EmployeeId);
        outbox.Received(1).Enqueue(Arg.Is<AssetAssigned>(e => e.AssetId == asset.Id && e.EmployeeId == EmployeeId && e.IssuedAt == issuedAt && e.AssetName == "MacBook Pro 14"));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssetAssignmentApprovalRejectedHandler_ForPendingAssignment_ReturnsToUnassigned()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.RequestAssignment(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var approvalRequestId = Guid.NewGuid();
        asset.AttachApproval(approvalRequestId);
        var assets = Substitute.For<IAssetRepository>();
        assets.FindByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Asset?>(asset));
        var unitOfWork = Substitute.For<IAssetsUnitOfWork>();
        var handler = new AssetAssignmentApprovalRejectedHandler(assets, unitOfWork);

        await handler.Handle(new ApprovalRejected(TenantId, approvalRequestId, ApprovalRequestTypes.AssetAssignment, asset.Id, EmployeeId, 1), CancellationToken.None);

        asset.Status.ShouldBe(AssetStatus.Unassigned);
        asset.AssignedEmployeeId.ShouldBeNull();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
        outbox.Received(1).Enqueue(Arg.Is<AssetReturned>(e => e.AssetId == asset.Id && e.EmployeeId == EmployeeId && e.ReturnedAt == returnedAt && e.AssetName == "MacBook Pro 14"));
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
        var handler = new GetMyAssetsHandler(currentUser, assets, Substitute.For<IAssetCategoryRepository>());

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
        var categories = Substitute.For<IAssetCategoryRepository>();
        var ppe = AssetCategory.Create(TenantId, "ppe", "KKD", CategoryId, replacementIntervalDays: 30).Value;
        categories.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AssetCategory> { ppe });
        var handler = new GetMyAssetsHandler(currentUser, assets, categories);

        var result = await handler.Handle(new GetMyAssetsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value[0].Id.ShouldBe(asset.Id);
        result.Value[0].Status.ShouldBe(AssetStatus.Assigned);
        result.Value[0].ReplacementDueOn.ShouldBe(asset.IssuedAt!.Value.AddDays(30));
    }
}
