using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

public sealed class AssetTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public void Create_WithEmptyName_Fails()
    {
        var result = Asset.Create(TenantId, CategoryId, "  ", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.NameRequired);
    }

    [Fact]
    public void Create_StartsUnassigned()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", "SN-001", "Mühendislik ekibi").Value;

        asset.Status.ShouldBe(AssetStatus.Unassigned);
        asset.AssignedEmployeeId.ShouldBeNull();
        asset.IssuedAt.ShouldBeNull();
        asset.ReturnedAt.ShouldBeNull();
    }

    [Fact]
    public void Update_ChangesEditableFields()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", "SN-001", null).Value;

        var updated = asset.Update("MacBook Pro 16", "SN-002", "Yeni açıklama");

        updated.IsSuccess.ShouldBeTrue();
        asset.Name.ShouldBe("MacBook Pro 16");
        asset.SerialNumber.ShouldBe("SN-002");
        asset.Description.ShouldBe("Yeni açıklama");
    }

    [Fact]
    public void Assign_WhenUnassigned_Succeeds()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var issuedAt = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = asset.Assign(EmployeeId, issuedAt);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Assigned);
        asset.AssignedEmployeeId.ShouldBe(EmployeeId);
        asset.IssuedAt.ShouldBe(issuedAt);
    }

    [Fact]
    public void Assign_WhenAlreadyAssigned_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        var result = asset.Assign(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAssigned);
    }

    [Fact]
    public void Assign_WhenRetired_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Retire();

        var result = asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAssigned);
    }

    /// <summary>Test helper: RequestAssignment -> AttachApproval -> CompleteAssignment, the full path a real approval takes.</summary>
    private static Guid AssignViaWorkflow(Asset asset, Guid employeeId, DateOnly issuedAt)
    {
        asset.RequestAssignment(employeeId, issuedAt);
        var approvalRequestId = Guid.NewGuid();
        asset.AttachApproval(approvalRequestId);
        asset.CompleteAssignment(approvalRequestId, issuedAt);
        return approvalRequestId;
    }

    [Fact]
    public void RequestAssignment_WhenUnassigned_MovesToPendingAssignment()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var issuedAt = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = asset.RequestAssignment(EmployeeId, issuedAt);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.PendingAssignment);
        asset.PendingEmployeeId.ShouldBe(EmployeeId);
        asset.PendingIssuedAt.ShouldBe(issuedAt);
        asset.AssignedEmployeeId.ShouldBeNull();
    }

    [Fact]
    public void RequestAssignment_WhenAlreadyAssigned_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        AssignViaWorkflow(asset, EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        var result = asset.RequestAssignment(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAssigned);
    }

    [Fact]
    public void RequestAssignment_WhenRetired_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Retire();

        var result = asset.RequestAssignment(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAssigned);
    }

    [Fact]
    public void RequestAssignment_WhenAlreadyPending_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.RequestAssignment(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        var result = asset.RequestAssignment(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAssigned);
    }

    [Fact]
    public void CompleteAssignment_WithMatchingApproval_Succeeds()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var issuedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        asset.RequestAssignment(EmployeeId, issuedAt);
        var approvalRequestId = Guid.NewGuid();
        asset.AttachApproval(approvalRequestId);

        var result = asset.CompleteAssignment(approvalRequestId, issuedAt);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Assigned);
        asset.AssignedEmployeeId.ShouldBe(EmployeeId);
        asset.IssuedAt.ShouldBe(issuedAt);
        asset.PendingEmployeeId.ShouldBeNull();
        asset.ApprovalRequestId.ShouldBeNull();
    }

    [Fact]
    public void CompleteAssignment_WithMismatchedApproval_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.RequestAssignment(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        asset.AttachApproval(Guid.NewGuid());

        var result = asset.CompleteAssignment(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AssignmentApprovalMismatch);
        asset.Status.ShouldBe(AssetStatus.PendingAssignment);
    }

    [Fact]
    public void CompleteAssignment_WhenNotPending_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;

        var result = asset.CompleteAssignment(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AssignmentApprovalMismatch);
    }

    [Fact]
    public void RejectAssignment_WithMatchingApproval_ReturnsToUnassigned()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.RequestAssignment(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var approvalRequestId = Guid.NewGuid();
        asset.AttachApproval(approvalRequestId);

        var result = asset.RejectAssignment(approvalRequestId);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Unassigned);
        asset.PendingEmployeeId.ShouldBeNull();
        asset.ApprovalRequestId.ShouldBeNull();
        asset.AssignedEmployeeId.ShouldBeNull();
    }

    [Fact]
    public void RejectAssignment_WithMismatchedApproval_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.RequestAssignment(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        asset.AttachApproval(Guid.NewGuid());

        var result = asset.RejectAssignment(Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.AssignmentApprovalMismatch);
        asset.Status.ShouldBe(AssetStatus.PendingAssignment);
    }

    [Fact]
    public void Return_WhenAssigned_ClearsAssignmentAndSetsReturnedAt()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));
        var returnedAt = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = asset.Return(returnedAt);

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Unassigned);
        asset.AssignedEmployeeId.ShouldBeNull();
        asset.IssuedAt.ShouldBeNull();
        asset.ReturnedAt.ShouldBe(returnedAt);
    }

    [Fact]
    public void Return_WhenNotAssigned_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;

        var result = asset.Return(DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.NotAssigned);
    }

    [Fact]
    public void Retire_WhenUnassigned_Succeeds()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;

        var result = asset.Retire();

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Retired);
    }

    [Fact]
    public void Retire_WhileAssigned_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.Assign(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        var result = asset.Retire();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.CannotRetireWhileAssigned);
    }

    [Fact]
    public void Retire_WhilePendingAssignment_Fails()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        asset.RequestAssignment(EmployeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        var result = asset.Retire();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.CannotRetireWhileAssigned);
    }

    [Fact]
    public void FullLifecycle_AssignReturnThenReassign_Succeeds()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var firstEmployee = Guid.NewGuid();
        var secondEmployee = Guid.NewGuid();

        asset.Assign(firstEmployee, DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.ShouldBeTrue();
        asset.Return(DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Unassigned);

        var requested = asset.RequestAssignment(secondEmployee, DateOnly.FromDateTime(DateTime.UtcNow));

        requested.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.PendingAssignment);
        asset.PendingEmployeeId.ShouldBe(secondEmployee);
    }
}
