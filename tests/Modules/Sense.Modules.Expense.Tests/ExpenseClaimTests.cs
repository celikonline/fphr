using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

public sealed class ExpenseClaimTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Submit_WithNoItems_Fails()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, "Müşteri ziyareti");

        var result = claim.Submit();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ClaimNoItems);
    }

    [Fact]
    public void AddItem_TaxInclusiveAmount_ComputesTaxAmount()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);

        var added = claim.AddItem(CategoryId, 120m, DateOnly.FromDateTime(DateTime.UtcNow), 20m, "TRY", null, "yemek harcaması");

        added.IsSuccess.ShouldBeTrue();
        // 120 is tax-inclusive at 20% VAT: tax = 120 - 120/1.20 = 20.00
        added.Value.TaxAmount.ShouldBe(20.00m);
    }

    [Fact]
    public void AddItem_TwiceThenSubmit_TotalIsSumOfItemsAndStatusBecomesSubmitted()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        var receiptDate = DateOnly.FromDateTime(DateTime.UtcNow);

        claim.AddItem(CategoryId, 100m, receiptDate, null, "TRY", null, "taxi").IsSuccess.ShouldBeTrue();
        claim.AddItem(CategoryId, 50m, receiptDate, null, "TRY", null, "lunch").IsSuccess.ShouldBeTrue();

        claim.TotalAmount.ShouldBe(150m);

        var submitted = claim.Submit();

        submitted.IsSuccess.ShouldBeTrue();
        claim.Status.ShouldBe(ExpenseClaimStatus.Submitted);
    }

    [Fact]
    public void RemoveItem_RecalculatesTotal()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        var receiptDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var item1 = claim.AddItem(CategoryId, 100m, receiptDate, null, "TRY", null, null).Value;
        claim.AddItem(CategoryId, 50m, receiptDate, null, "TRY", null, null);

        var removed = claim.RemoveItem(item1.Id);

        removed.IsSuccess.ShouldBeTrue();
        claim.TotalAmount.ShouldBe(50m);
    }

    [Fact]
    public void AddItem_AfterSubmit_Fails()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        var receiptDate = DateOnly.FromDateTime(DateTime.UtcNow);
        claim.AddItem(CategoryId, 100m, receiptDate, null, "TRY", null, null);
        claim.Submit();

        var result = claim.AddItem(CategoryId, 10m, receiptDate, null, "TRY", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ClaimNotDraft);
    }

    [Fact]
    public void Approve_WrongApprovalRequestId_Fails()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        claim.AddItem(CategoryId, 100m, DateOnly.FromDateTime(DateTime.UtcNow), null, "TRY", null, null);
        claim.Submit();
        claim.AttachApproval(Guid.NewGuid(), DateTime.UtcNow);

        var result = claim.Approve(Guid.NewGuid(), DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ClaimNotSubmitted);
    }

    [Fact]
    public void FullLifecycle_SubmitApproveThenPay_Succeeds()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        claim.AddItem(CategoryId, 100m, DateOnly.FromDateTime(DateTime.UtcNow), null, "TRY", null, null);
        claim.Submit();
        var approvalId = Guid.NewGuid();
        claim.AttachApproval(approvalId, DateTime.UtcNow);

        claim.Approve(approvalId, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        claim.Status.ShouldBe(ExpenseClaimStatus.Approved);

        claim.MarkPaid(DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        claim.Status.ShouldBe(ExpenseClaimStatus.Paid);
    }
}
