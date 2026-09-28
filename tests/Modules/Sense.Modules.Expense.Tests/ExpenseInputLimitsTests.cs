using NSubstitute;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Expense.Application.Advances;
using Sense.Modules.Expense.Application.ExpenseClaims;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

/// <summary>QA teslim testi bulgu 11: amount cap, ISO 4217 whitelist, one currency per claim, no future receipt dates.</summary>
public sealed class ExpenseInputLimitsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Theory]
    [InlineData(-31, false)]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void AdvanceValidator_RequestedDateOutsideTheWindow_IsRejected(int dayOffset, bool valid)
    {
        var command = new CreateAdvanceRequestCommand(1_000m, "TRY", Today.AddDays(dayOffset), false, null, null);

        var result = new CreateAdvanceRequestValidator().Validate(command);

        result.IsValid.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.ShouldContain(e => e.ErrorMessage == ExpenseDomainErrors.AdvanceDateOutOfRange);
        }
    }

    [Fact]
    public void AdvanceValidator_OmittedRequestedDate_IsAccepted()
    {
        var command = new CreateAdvanceRequestCommand(1_000m, "TRY", null, false, null, null);

        new CreateAdvanceRequestValidator().Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void AddItem_AmountAboveTheCap_IsRejected()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);

        var result = claim.AddItem(CategoryId, 99_999_999_999m, Today, null, "TRY", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ItemAmountTooLarge);
    }

    [Fact]
    public void AddItem_AmountExactlyAtTheCap_IsAccepted()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);

        claim.AddItem(CategoryId, ExpenseLimits.MaxAmount, Today, null, "TRY", null, null).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("XXX")]
    [InlineData("ABC")]
    [InlineData("tr1")]
    public void AddItem_UnknownCurrency_IsRejected(string currency)
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);

        var result = claim.AddItem(CategoryId, 100m, Today, null, currency, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ItemCurrencyUnsupported);
    }

    [Theory]
    [InlineData("TRY")]
    [InlineData("usd")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    public void AddItem_KnownCurrency_IsAccepted(string currency)
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);

        claim.AddItem(CategoryId, 100m, Today, null, currency, null, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AddItem_ADifferentCurrencyThanTheClaimsOtherItems_IsRejected()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        claim.AddItem(CategoryId, 100m, Today, null, "TRY", null, null).IsSuccess.ShouldBeTrue();

        var result = claim.AddItem(CategoryId, 10m, Today, null, "USD", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ClaimCurrencyMismatch);
        claim.Items.Count.ShouldBe(1);
    }

    [Fact]
    public void UpdateItem_SwitchingTheOnlyItemToAnotherCurrency_IsAllowed_ButNotWhenOthersDiffer()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        var first = claim.AddItem(CategoryId, 100m, Today, null, "TRY", null, null).Value;
        var second = claim.AddItem(CategoryId, 50m, Today, null, "TRY", null, null).Value;

        claim.UpdateItem(first.Id, CategoryId, 100m, Today, null, "USD", null, null).Error.Code.ShouldBe(ExpenseDomainErrors.ClaimCurrencyMismatch);

        claim.RemoveItem(second.Id).IsSuccess.ShouldBeTrue();
        claim.UpdateItem(first.Id, CategoryId, 100m, Today, null, "USD", null, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AdvanceSubmit_50Million_IsRejected_And_UnknownCurrencyIsRejected()
    {
        AdvanceRequest.Submit(TenantId, EmployeeId, 50_000_000m, "TRY", Today, false, null, null).Error.Code
            .ShouldBe(ExpenseDomainErrors.AdvanceAmountTooLarge);
        AdvanceRequest.Submit(TenantId, EmployeeId, 1_000m, "XXX", Today, false, null, null).Error.Code
            .ShouldBe(ExpenseDomainErrors.ItemCurrencyUnsupported);
        AdvanceRequest.Submit(TenantId, EmployeeId, 1_000m, "TRY", Today, false, null, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AddItemHandler_AReceiptDatedInTheFuture_IsRejected()
    {
        var claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
        var claims = Substitute.For<IExpenseClaimRepository>();
        claims.GetByIdAsync(claim.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Result<ExpenseClaim>>(claim));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(EmployeeId);
        var handler = new AddExpenseItemHandler(
            Substitute.For<ITenantContext>(), currentUser, claims, Substitute.For<IExpenseCategoryRepository>(),
            Substitute.For<IDocumentAttachmentLookup>(), TimeProvider.System);

        var result = await handler.Handle(
            new AddExpenseItemCommand(claim.Id, CategoryId, 100m, Today.AddDays(30), null, "TRY", null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ItemReceiptDateInFuture);
        claim.Items.ShouldBeEmpty();
    }
}
