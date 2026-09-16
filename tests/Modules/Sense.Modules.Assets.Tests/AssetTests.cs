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
    public void FullLifecycle_AssignReturnThenReassign_Succeeds()
    {
        var asset = Asset.Create(TenantId, CategoryId, "MacBook Pro 14", null, null).Value;
        var firstEmployee = Guid.NewGuid();
        var secondEmployee = Guid.NewGuid();

        asset.Assign(firstEmployee, DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.ShouldBeTrue();
        asset.Return(DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Unassigned);

        var reassigned = asset.Assign(secondEmployee, DateOnly.FromDateTime(DateTime.UtcNow));

        reassigned.IsSuccess.ShouldBeTrue();
        asset.AssignedEmployeeId.ShouldBe(secondEmployee);
    }
}
