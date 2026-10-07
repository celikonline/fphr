using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>Zimmet v2 domain rules: assignment ledger, return condition, loss, acknowledgement and künye validation.</summary>
public sealed class AssetLedgerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Issued = new(2026, 9, 1);
    private static readonly DateTime Now = new(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);

    private static Asset NewAsset() => Asset.Create(TenantId, CategoryId, "MacBook Pro 14", "SN-1", null).Value;

    private static Asset AssignedAsset()
    {
        var asset = NewAsset();
        asset.Assign(EmployeeId, Issued).IsSuccess.ShouldBeTrue();
        return asset;
    }

    // ---- ledger: open / close ----

    [Fact]
    public void Assign_OpensExactlyOneLedgerRow()
    {
        var asset = AssignedAsset();

        var row = asset.Assignments.ShouldHaveSingleItem();
        row.IsOpen.ShouldBeTrue();
        row.EmployeeId.ShouldBe(EmployeeId);
        row.IssuedAt.ShouldBe(Issued);
        row.AssetId.ShouldBe(asset.Id);
        row.TenantId.ShouldBe(TenantId);
        row.AcknowledgedAt.ShouldBeNull();
        asset.OpenAssignment.ShouldBeSameAs(row);
    }

    [Fact]
    public void Assign_WhenAlreadyAssigned_DoesNotOpenASecondRow()
    {
        var asset = AssignedAsset();

        asset.Assign(Guid.NewGuid(), Issued.AddDays(1)).IsFailure.ShouldBeTrue();

        asset.Assignments.Count(a => a.IsOpen).ShouldBe(1);
    }

    [Fact]
    public void CompleteAssignment_OpensRowForThePendingEmployee()
    {
        var asset = NewAsset();
        asset.RequestAssignment(EmployeeId, Issued);
        var approvalId = Guid.NewGuid();
        asset.AttachApproval(approvalId);

        asset.CompleteAssignment(approvalId, Issued.AddDays(5)).IsSuccess.ShouldBeTrue();

        var row = asset.Assignments.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(EmployeeId);
        row.IssuedAt.ShouldBe(Issued);
        row.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void RejectAssignment_OpensNoLedgerRow()
    {
        var asset = NewAsset();
        asset.RequestAssignment(EmployeeId, Issued);
        var approvalId = Guid.NewGuid();
        asset.AttachApproval(approvalId);

        asset.RejectAssignment(approvalId).IsSuccess.ShouldBeTrue();

        asset.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public void Return_ClosesTheOpenRowWithConditionAndTrimmedNote()
    {
        var asset = AssignedAsset();

        var result = asset.Return(Issued.AddDays(10), AssetCondition.Damaged, "  Ekran çatlak  ");

        result.IsSuccess.ShouldBeTrue();
        var row = asset.Assignments.ShouldHaveSingleItem();
        row.IsOpen.ShouldBeFalse();
        row.ReturnedAt.ShouldBe(Issued.AddDays(10));
        row.EndReason.ShouldBe(AssignmentEndReason.Returned);
        row.ReturnCondition.ShouldBe(AssetCondition.Damaged);
        row.Note.ShouldBe("Ekran çatlak");
        asset.Condition.ShouldBe(AssetCondition.Damaged);
        asset.OpenAssignment.ShouldBeNull();
        asset.Status.ShouldBe(AssetStatus.Unassigned);
    }

    [Fact]
    public void Return_WithoutCondition_KeepsAndRecordsTheCurrentCondition()
    {
        var asset = NewAsset();
        asset.Update("MacBook Pro 14", "SN-1", null, condition: AssetCondition.Fair);
        asset.Assign(EmployeeId, Issued);

        asset.Return(Issued.AddDays(1)).IsSuccess.ShouldBeTrue();

        asset.Condition.ShouldBe(AssetCondition.Fair);
        asset.Assignments.Single().ReturnCondition.ShouldBe(AssetCondition.Fair);
        asset.Assignments.Single().Note.ShouldBeNull();
    }

    [Fact]
    public void Return_WithBlankNote_StoresNoNote()
    {
        var asset = AssignedAsset();

        asset.Return(Issued, AssetCondition.Good, "   ").IsSuccess.ShouldBeTrue();

        asset.Assignments.Single().Note.ShouldBeNull();
    }

    [Fact]
    public void Return_NoteOfExactlyMaxLength_Succeeds()
    {
        var asset = AssignedAsset();

        asset.Return(Issued, AssetCondition.Good, new string('n', AssetsLimits.NoteMaxLength)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Return_NoteOneOverMaxLength_FailsAndLeavesTheRowOpen()
    {
        var asset = AssignedAsset();

        var result = asset.Return(Issued, AssetCondition.Good, new string('n', AssetsLimits.NoteMaxLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.NoteTooLong);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        asset.Status.ShouldBe(AssetStatus.Assigned);
        asset.OpenAssignment.ShouldNotBeNull();
    }

    [Fact]
    public void Return_OnTheIssueDate_Succeeds_OneDayBeforeFails()
    {
        AssignedAsset().Return(Issued).IsSuccess.ShouldBeTrue();

        var asset = AssignedAsset();
        var result = asset.Return(Issued.AddDays(-1));

        result.Error.Code.ShouldBe(AssetsDomainErrors.ReturnBeforeIssue);
        asset.OpenAssignment.ShouldNotBeNull();
    }

    [Fact]
    public void Return_WhenNotAssigned_Fails()
    {
        NewAsset().Return(Issued).Error.Code.ShouldBe(AssetsDomainErrors.NotAssigned);
    }

    [Fact]
    public void ReassignAfterReturn_KeepsHistoryAndHasOneOpenRow()
    {
        var asset = AssignedAsset();
        asset.Return(Issued.AddDays(3));
        var second = Guid.NewGuid();

        asset.Assign(second, Issued.AddDays(7)).IsSuccess.ShouldBeTrue();

        asset.Assignments.Count.ShouldBe(2);
        asset.Assignments.Count(a => a.IsOpen).ShouldBe(1);
        asset.OpenAssignment!.EmployeeId.ShouldBe(second);
    }

    // ---- loss ----

    [Fact]
    public void MarkLost_WhenAssigned_ClosesTheRowWithReasonLostAndNoCondition()
    {
        var asset = AssignedAsset();

        var result = asset.MarkLost(Issued.AddDays(4), "Servis aracında kayboldu");

        result.IsSuccess.ShouldBeTrue();
        asset.Status.ShouldBe(AssetStatus.Lost);
        asset.AssignedEmployeeId.ShouldBeNull();
        asset.IssuedAt.ShouldBeNull();
        var row = asset.Assignments.ShouldHaveSingleItem();
        row.EndReason.ShouldBe(AssignmentEndReason.Lost);
        row.ReturnedAt.ShouldBe(Issued.AddDays(4));
        row.ReturnCondition.ShouldBeNull();
        row.Note.ShouldBe("Servis aracında kayboldu");
    }

    [Fact]
    public void MarkLost_WhenUnassigned_GoesLostWithoutLedgerRow()
    {
        var asset = NewAsset();

        asset.MarkLost(Issued).IsSuccess.ShouldBeTrue();

        asset.Status.ShouldBe(AssetStatus.Lost);
        asset.Assignments.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AssetStatus.Retired)]
    [InlineData(AssetStatus.Lost)]
    [InlineData(AssetStatus.PendingAssignment)]
    public void MarkLost_FromOtherStatuses_FailsWithCannotLose(AssetStatus status)
    {
        var asset = NewAsset();
        switch (status)
        {
            case AssetStatus.Retired:
                asset.Retire();
                break;
            case AssetStatus.Lost:
                asset.MarkLost(Issued);
                break;
            case AssetStatus.PendingAssignment:
                asset.RequestAssignment(EmployeeId, Issued);
                break;
        }

        var result = asset.MarkLost(Issued);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AssetsDomainErrors.CannotLose);
        result.Error.Type.ShouldBe(ErrorType.Rule);
        asset.Status.ShouldBe(status);
    }

    [Fact]
    public void MarkLost_BeforeIssueDate_Fails_OnIssueDateSucceeds()
    {
        var asset = AssignedAsset();
        asset.MarkLost(Issued.AddDays(-1)).Error.Code.ShouldBe(AssetsDomainErrors.ReturnBeforeIssue);
        asset.OpenAssignment.ShouldNotBeNull();

        asset.MarkLost(Issued).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void MarkLost_NoteOneOverMaxLength_Fails()
    {
        var asset = AssignedAsset();

        var result = asset.MarkLost(Issued, new string('x', AssetsLimits.NoteMaxLength + 1));

        result.Error.Code.ShouldBe(AssetsDomainErrors.NoteTooLong);
        asset.Status.ShouldBe(AssetStatus.Assigned);
    }

    [Fact]
    public void LostAsset_CannotBeAssignedOrReturned()
    {
        var asset = NewAsset();
        asset.MarkLost(Issued);

        asset.Assign(EmployeeId, Issued).IsFailure.ShouldBeTrue();
        asset.Return(Issued).Error.Code.ShouldBe(AssetsDomainErrors.NotAssigned);
    }

    [Fact]
    public void AssetStatusLost_HasTheContractValue()
    {
        ((int)AssetStatus.Lost).ShouldBe(4);
    }

    // ---- acknowledge ----

    [Fact]
    public void Acknowledge_ByTheAssignee_StampsTheOpenRow()
    {
        var asset = AssignedAsset();

        asset.Acknowledge(EmployeeId, Now).IsSuccess.ShouldBeTrue();

        asset.OpenAssignment!.AcknowledgedAt.ShouldBe(Now);
    }

    [Fact]
    public void Acknowledge_ByAnotherEmployee_IsForbiddenAndStampsNothing()
    {
        var asset = AssignedAsset();

        var result = asset.Acknowledge(Guid.NewGuid(), Now);

        result.Error.Code.ShouldBe(AssetsDomainErrors.NotAssignee);
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        asset.OpenAssignment!.AcknowledgedAt.ShouldBeNull();
    }

    [Fact]
    public void Acknowledge_Twice_FailsWithAlreadyAcknowledgedAndKeepsTheFirstTimestamp()
    {
        var asset = AssignedAsset();
        asset.Acknowledge(EmployeeId, Now);

        var result = asset.Acknowledge(EmployeeId, Now.AddHours(1));

        result.Error.Code.ShouldBe(AssetsDomainErrors.AlreadyAcknowledged);
        result.Error.Type.ShouldBe(ErrorType.Rule);
        asset.OpenAssignment!.AcknowledgedAt.ShouldBe(Now);
    }

    [Fact]
    public void Acknowledge_WhenNotAssigned_Fails()
    {
        NewAsset().Acknowledge(EmployeeId, Now).Error.Code.ShouldBe(AssetsDomainErrors.NotAssigned);
    }

    [Fact]
    public void Acknowledge_AfterReturn_FailsAndANewAssignmentStartsUnacknowledged()
    {
        var asset = AssignedAsset();
        asset.Acknowledge(EmployeeId, Now);
        asset.Return(Issued.AddDays(2));

        asset.Acknowledge(EmployeeId, Now).Error.Code.ShouldBe(AssetsDomainErrors.NotAssigned);

        asset.Assign(EmployeeId, Issued.AddDays(9));
        asset.OpenAssignment!.AcknowledgedAt.ShouldBeNull();
    }

    // ---- künye ----

    [Fact]
    public void Create_WithDetails_TrimsAndStoresThem_AndStartsGood()
    {
        var details = new AssetDetails("  DMB-001 ", " Apple ", " M3 ", new DateOnly(2026, 1, 10), 54999.90m, new DateOnly(2028, 1, 10));

        var asset = Asset.Create(TenantId, CategoryId, "MacBook", "SN-1", null, details).Value;

        asset.AssetTag.ShouldBe("DMB-001");
        asset.Brand.ShouldBe("Apple");
        asset.Model.ShouldBe("M3");
        asset.PurchaseDate.ShouldBe(new DateOnly(2026, 1, 10));
        asset.PurchaseCost.ShouldBe(54999.90m);
        asset.WarrantyEndsOn.ShouldBe(new DateOnly(2028, 1, 10));
        asset.Condition.ShouldBe(AssetCondition.Good);
    }

    [Fact]
    public void Create_WithoutDetails_LeavesKunyeEmpty()
    {
        var asset = NewAsset();

        asset.AssetTag.ShouldBeNull();
        asset.PurchaseCost.ShouldBeNull();
        asset.WarrantyEndsOn.ShouldBeNull();
    }

    [Fact]
    public void Create_ZeroCost_Succeeds_NegativeCostFails()
    {
        Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails(null, null, null, null, 0m, null)).IsSuccess.ShouldBeTrue();

        var result = Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails(null, null, null, null, -0.01m, null));

        result.Error.Code.ShouldBe(AssetsDomainErrors.CostInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_WarrantySameDayAsPurchase_Succeeds_DayBeforeFails()
    {
        var purchase = new DateOnly(2026, 3, 1);

        Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails(null, null, null, purchase, null, purchase)).IsSuccess.ShouldBeTrue();

        var result = Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails(null, null, null, purchase, null, purchase.AddDays(-1)));

        result.Error.Code.ShouldBe(AssetsDomainErrors.WarrantyBeforePurchase);
    }

    [Fact]
    public void Create_WarrantyWithoutPurchaseDate_IsAccepted()
    {
        Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails(null, null, null, null, null, new DateOnly(2020, 1, 1))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_WithDetails_ReplacesThem_AndConditionIsSet()
    {
        var asset = Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails("T-1", "Dell", "XPS", null, 10m, null)).Value;

        var result = asset.Update("A2", null, null, new AssetDetails(null, "Lenovo", null, null, null, null), AssetCondition.Damaged);

        result.IsSuccess.ShouldBeTrue();
        asset.AssetTag.ShouldBeNull();
        asset.Brand.ShouldBe("Lenovo");
        asset.Model.ShouldBeNull();
        asset.PurchaseCost.ShouldBeNull();
        asset.Condition.ShouldBe(AssetCondition.Damaged);
    }

    [Fact]
    public void Update_WithoutDetailsOrCondition_LeavesThemUntouched()
    {
        var asset = Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails("T-1", "Dell", null, null, null, null)).Value;
        asset.Update("A", null, null, condition: AssetCondition.Fair);

        asset.Update("A2", null, null).IsSuccess.ShouldBeTrue();

        asset.AssetTag.ShouldBe("T-1");
        asset.Brand.ShouldBe("Dell");
        asset.Condition.ShouldBe(AssetCondition.Fair);
    }

    [Fact]
    public void Update_InvalidDetails_FailsAndChangesNothing()
    {
        var asset = Asset.Create(TenantId, CategoryId, "A", null, null, new AssetDetails("T-1", null, null, null, null, null)).Value;

        var result = asset.Update("B", null, null, new AssetDetails("T-2", null, null, null, -1m, null));

        result.Error.Code.ShouldBe(AssetsDomainErrors.CostInvalid);
        asset.Name.ShouldBe("A");
        asset.AssetTag.ShouldBe("T-1");
    }
}
