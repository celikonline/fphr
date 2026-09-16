using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.Advances;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

public sealed class AdvanceRequestTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public void Submit_ZeroAmount_Fails()
    {
        var result = AdvanceRequest.Submit(TenantId, EmployeeId, 0m, "TRY", DateOnly.FromDateTime(DateTime.UtcNow), false, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.AdvanceAmountInvalid);
    }

    [Fact]
    public void Submit_InstallmentRequestedWithoutCount_Fails()
    {
        var result = AdvanceRequest.Submit(TenantId, EmployeeId, 1000m, "TRY", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.AdvanceInstallmentCountInvalid);
    }

    [Fact]
    public void Submit_InstallmentCountAboveMax_Fails()
    {
        var result = AdvanceRequest.Submit(TenantId, EmployeeId, 1000m, "TRY", DateOnly.FromDateTime(DateTime.UtcNow), true, ExpenseLimits.MaxInstallments + 1, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.AdvanceInstallmentCountInvalid);
    }

    [Fact]
    public void Submit_AmountOnly_SucceedsAsSubmitted()
    {
        var result = AdvanceRequest.Submit(TenantId, EmployeeId, 5000m, "try", DateOnly.FromDateTime(DateTime.UtcNow), false, null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(AdvanceRequestStatus.Submitted);
        result.Value.Currency.ShouldBe("TRY");
    }

    [Fact]
    public void FullLifecycle_ApproveThenPay_Succeeds()
    {
        var advance = AdvanceRequest.Submit(TenantId, EmployeeId, 5000m, "TRY", DateOnly.FromDateTime(DateTime.UtcNow), false, null, null).Value;
        var approvalId = Guid.NewGuid();
        advance.AttachApproval(approvalId);

        advance.Approve(approvalId, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        advance.Status.ShouldBe(AdvanceRequestStatus.Approved);

        var payerId = Guid.NewGuid();
        advance.MarkPaid(DateTime.UtcNow, payerId, "Banka havalesi").IsSuccess.ShouldBeTrue();
        advance.Status.ShouldBe(AdvanceRequestStatus.Paid);
        advance.PaidByEmployeeId.ShouldBe(payerId);
        advance.PaymentNote.ShouldBe("Banka havalesi");
    }

    [Fact]
    public void MarkPaid_WhenNotApproved_Fails()
    {
        var advance = AdvanceRequest.Submit(TenantId, EmployeeId, 5000m, "TRY", DateOnly.FromDateTime(DateTime.UtcNow), false, null, null).Value;

        var result = advance.MarkPaid(DateTime.UtcNow, Guid.NewGuid(), null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.AdvanceAlreadyPaid);
    }
}
