using NSubstitute;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Expense.Application;
using Sense.Modules.Expense.Application.ExpenseClaims;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

/// <summary>
/// Payroll pays items with a non-null ReceiptFileId as tax-free reimbursement (IApprovedExpensePayoutReader), so a
/// self-service caller must not be able to flag an item as receipted with an id that isn't their own uploaded document.
/// </summary>
public sealed class ExpenseReceiptValidationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ExpenseCategory category = ExpenseCategory.Create(TenantId, "TAXI", "expense.category.taxi", null).Value;
    private readonly ExpenseClaim claim = ExpenseClaim.Create(TenantId, EmployeeId, null);
    private readonly IExpenseClaimRepository claims = Substitute.For<IExpenseClaimRepository>();
    private readonly IExpenseCategoryRepository categories = Substitute.For<IExpenseCategoryRepository>();
    private readonly IDocumentAttachmentLookup documents = Substitute.For<IDocumentAttachmentLookup>();
    private readonly ITenantContext tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser currentUser = Substitute.For<ICurrentUser>();

    public ExpenseReceiptValidationTests()
    {
        claims.GetByIdAsync(claim.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Result<ExpenseClaim>>(claim));
        categories.GetByIdAsync(category.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Result<ExpenseCategory>>(category));
        tenant.TenantId.Returns(TenantId);
        currentUser.EmployeeId.Returns(EmployeeId);
        currentUser.UserId.Returns(UserId);
        documents.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, AttachedDocumentInfo>>(new Dictionary<Guid, AttachedDocumentInfo>()));
    }

    private Guid GivenDocument(Guid uploadedBy, Guid? employeeId)
    {
        var documentId = Guid.NewGuid();
        documents.GetActiveAsync(TenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(documentId)), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, AttachedDocumentInfo>>(new Dictionary<Guid, AttachedDocumentInfo>
            {
                [documentId] = new(documentId, "fis.pdf", "application/pdf", 1024, uploadedBy, employeeId),
            }));
        return documentId;
    }

    private Task<Result<Guid>> AddItem(string? receiptFileId) =>
        new AddExpenseItemHandler(tenant, currentUser, claims, categories, documents, TimeProvider.System).Handle(
            new AddExpenseItemCommand(claim.Id, category.Id, 100m, Today, null, "TRY", receiptFileId, null), CancellationToken.None);

    private Task<Result> UpdateItem(Guid itemId, string? receiptFileId) =>
        new UpdateExpenseItemHandler(tenant, currentUser, claims, documents, TimeProvider.System).Handle(
            new UpdateExpenseItemCommand(claim.Id, itemId, category.Id, 250m, Today, null, "TRY", receiptFileId, null), CancellationToken.None);

    public static TheoryData<string> RejectedReceiptIds => new()
    {
        { "not-a-guid" },
        { "../../other-tenant/receipt.pdf" },
        { Guid.NewGuid().ToString() },
    };

    [Theory]
    [MemberData(nameof(RejectedReceiptIds))]
    public async Task AddItem_ReceiptIdThatIsNotAnActiveDocumentOfThisTenant_IsRejected(string receiptFileId)
    {
        var result = await AddItem(receiptFileId);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseErrors.ReceiptNotFound);
        claim.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddItem_AnotherEmployeesDocument_IsRejected()
    {
        var foreign = GivenDocument(uploadedBy: OtherUserId, employeeId: OtherEmployeeId);

        var result = await AddItem(foreign.ToString());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseErrors.ReceiptNotFound);
        claim.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddItem_ADocumentTheCallerUploadedForAnotherEmployee_IsRejected()
    {
        var uploadedForSomeoneElse = GivenDocument(uploadedBy: UserId, employeeId: OtherEmployeeId);

        var result = await AddItem(uploadedForSomeoneElse.ToString());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseErrors.ReceiptNotFound);
    }

    [Fact]
    public async Task AddItem_OwnSelfServiceUpload_IsAcceptedAndStoredInCanonicalForm()
    {
        var own = GivenDocument(uploadedBy: UserId, employeeId: null);

        var result = await AddItem($"  {own.ToString().ToUpperInvariant()}  ");

        result.IsSuccess.ShouldBeTrue();
        claim.Items.Single().ReceiptFileId.ShouldBe(own.ToString());
    }

    [Fact]
    public async Task AddItem_ADocumentFiledToTheClaimsEmployee_IsAccepted()
    {
        var filedByHr = GivenDocument(uploadedBy: OtherUserId, employeeId: EmployeeId);

        var result = await AddItem(filedByHr.ToString());

        result.IsSuccess.ShouldBeTrue();
        claim.Items.Single().ReceiptFileId.ShouldBe(filedByHr.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AddItem_WithoutReceipt_IsAcceptedWithoutADocumentLookup(string? receiptFileId)
    {
        var result = await AddItem(receiptFileId);

        result.IsSuccess.ShouldBeTrue();
        claim.Items.Single().ReceiptFileId.ShouldBeNull();
        await documents.DidNotReceiveWithAnyArgs().GetActiveAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdateItem_AnotherEmployeesDocument_IsRejectedAndLeavesTheItemUnchanged()
    {
        var item = claim.AddItem(category.Id, 100m, Today, null, "TRY", null, null).Value;
        var foreign = GivenDocument(uploadedBy: OtherUserId, employeeId: OtherEmployeeId);

        var result = await UpdateItem(item.Id, foreign.ToString());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseErrors.ReceiptNotFound);
        item.ReceiptFileId.ShouldBeNull();
        item.Amount.ShouldBe(100m);
    }

    [Fact]
    public async Task UpdateItem_NonexistentDocument_IsRejected()
    {
        var item = claim.AddItem(category.Id, 100m, Today, null, "TRY", null, null).Value;

        var result = await UpdateItem(item.Id, Guid.NewGuid().ToString());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseErrors.ReceiptNotFound);
        item.ReceiptFileId.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateItem_OwnDocument_IsAccepted()
    {
        var item = claim.AddItem(category.Id, 100m, Today, null, "TRY", null, null).Value;
        var own = GivenDocument(uploadedBy: UserId, employeeId: null);

        var result = await UpdateItem(item.Id, own.ToString());

        result.IsSuccess.ShouldBeTrue();
        item.ReceiptFileId.ShouldBe(own.ToString());
        item.Amount.ShouldBe(250m);
    }

    [Fact]
    public async Task UpdateItem_ClearingTheReceipt_IsAccepted()
    {
        var own = GivenDocument(uploadedBy: UserId, employeeId: null);
        var item = claim.AddItem(category.Id, 100m, Today, null, "TRY", own.ToString(), null).Value;

        var result = await UpdateItem(item.Id, null);

        result.IsSuccess.ShouldBeTrue();
        item.ReceiptFileId.ShouldBeNull();
    }
}
