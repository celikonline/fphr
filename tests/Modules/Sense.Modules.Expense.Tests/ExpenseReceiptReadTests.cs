using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Expense.Application;
using Sense.Modules.Expense.Application.ExpenseClaims;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Modules.Expense.Infrastructure;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

public sealed class ExpenseReceiptReadTests
{
    [Fact]
    public void CoreServices_DoNotRegisterApiOnlyReadAccess()
    {
        var services = new ServiceCollection();
        services.AddExpenseCoreServices();
        services.Any(s => s.ServiceType == typeof(ExpenseClaimReadAccess)).ShouldBeFalse();
    }

    private readonly ITenantContext tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();
    private readonly IPermissionService permissions = Substitute.For<IPermissionService>();
    private readonly IDataScopeService scope = Substitute.For<IDataScopeService>();
    private readonly IApprovalRequestReadAccess workflow = Substitute.For<IApprovalRequestReadAccess>();
    private readonly IExpenseClaimRepository claims = Substitute.For<IExpenseClaimRepository>();
    private readonly IExpenseCategoryRepository categories = Substitute.For<IExpenseCategoryRepository>();
    private readonly IDocumentAttachmentLookup documents = Substitute.For<IDocumentAttachmentLookup>();
    private readonly ExpenseClaim claim = ExpenseClaim.Create(Guid.NewGuid(), Guid.NewGuid(), null);
    private readonly Guid caller = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid documentId = Guid.NewGuid();

    public ExpenseReceiptReadTests()
    {
        tenant.TenantId.Returns(claim.TenantId);
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.EmployeeId.Returns(caller);
        claims.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ExpenseClaim>(Error.NotFound(ExpenseDomainErrors.ClaimNotFound)));
        claims.GetByIdAsync(claim.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(claim));
        categories.ListAsync(true, Arg.Any<CancellationToken>()).Returns([]);
        claim.AddItem(Guid.NewGuid(), 120m, DateOnly.FromDateTime(DateTime.UtcNow), 20m, "TRY", documentId.ToString(), null);
        claim.AttachApproval(Guid.NewGuid(), DateTime.UtcNow);
        documents.GetActiveAsync(claim.TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, AttachedDocumentInfo> { [documentId] = new(documentId, "receipt.pdf", "application/pdf", 500, userId, claim.EmployeeId) });
        documents.GetDownloadUrlAsync(claim.TenantId, documentId, Arg.Any<CancellationToken>())
            .Returns(new AttachedDocumentDownloadUrl("https://storage.example/receipt", DateTime.UtcNow.AddMinutes(5), "receipt.pdf", "application/pdf"));
    }

    private ExpenseClaimReadAccess Access() => new(tenant, user, permissions, scope, workflow);
    private Task<Result<ExpenseClaimDto>> Review() => new ReviewExpenseClaimHandler(claims, categories, documents, tenant, Access())
        .Handle(new(claim.Id), CancellationToken.None);
    private Task<Result<AttachedDocumentDownloadUrl>> Download(Guid? itemId = null, Guid? attachmentId = null) =>
        new DownloadExpenseReceiptHandler(claims, documents, tenant, Access()).Handle(
            new(claim.Id, itemId ?? claim.Items.Single().Id, attachmentId ?? documentId), CancellationToken.None);

    [Theory]
    [InlineData("owner", true)]
    [InlineData("party", true)]
    [InlineData("hr", true)]
    [InlineData("hr-outside-scope", false)]
    [InlineData("outsider", false)]
    [InlineData("owner-no-permission", false)]
    [InlineData("unauthenticated", false)]
    public async Task ReviewAndDownload_RequireRecordReadAccess(string actor, bool allowed)
    {
        if (actor.StartsWith("owner", StringComparison.Ordinal) || actor == "unauthenticated") user.EmployeeId.Returns(claim.EmployeeId);
        if (actor == "unauthenticated") user.IsAuthenticated.Returns(false);
        permissions.HasAsync(userId, "expense.my.claims", Arg.Any<CancellationToken>()).Returns(actor is "owner" or "unauthenticated");
        permissions.HasAsync(userId, "expense.claims.view", Arg.Any<CancellationToken>()).Returns(actor.StartsWith("hr", StringComparison.Ordinal));
        scope.CanAccessEmployeeAsync("expense.claims.view", claim.EmployeeId, Arg.Any<CancellationToken>()).Returns(actor == "hr");
        workflow.CanReadAsync(claim.TenantId, claim.ApprovalRequestId!.Value, ExpenseRequestTypes.Claim, claim.Id,
            Arg.Any<Guid?>(), userId, Arg.Any<CancellationToken>()).Returns(actor == "party");

        var review = await Review();
        var download = await Download();
        review.IsSuccess.ShouldBe(allowed);
        download.IsSuccess.ShouldBe(allowed);
        if (allowed)
        {
            review.Value.Items.Single().ReceiptAttachments.Single().FileName.ShouldBe("receipt.pdf");
            download.Value.ContentType.ShouldBe("application/pdf");
        }
        else await documents.DidNotReceiveWithAnyArgs().GetDownloadUrlAsync(default, default, default);
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-item")]
    [InlineData("not-attached")]
    [InlineData("deleted")]
    public async Task Download_InvalidRecordOrAttachment_IsRejected(string scenario)
    {
        user.EmployeeId.Returns(claim.EmployeeId);
        permissions.HasAsync(userId, "expense.my.claims", Arg.Any<CancellationToken>()).Returns(true);
        if (scenario == "foreign-tenant") tenant.TenantId.Returns(Guid.NewGuid());
        if (scenario == "deleted") documents.GetDownloadUrlAsync(claim.TenantId, documentId, Arg.Any<CancellationToken>()).Returns((AttachedDocumentDownloadUrl?)null);
        var result = await Download(scenario == "foreign-item" ? Guid.NewGuid() : null, scenario == "not-attached" ? Guid.NewGuid() : null);
        result.IsFailure.ShouldBeTrue();
        if (scenario != "deleted") await documents.DidNotReceiveWithAnyArgs().GetDownloadUrlAsync(default, default, default);
    }

    [Fact]
    public async Task Download_UnknownClaim_ReturnsNotFoundWithoutDocumentLookup()
    {
        var result = await new DownloadExpenseReceiptHandler(claims, documents, tenant, Access()).Handle(
            new(Guid.NewGuid(), claim.Items.Single().Id, documentId), CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExpenseDomainErrors.ClaimNotFound);
        await documents.DidNotReceiveWithAnyArgs().GetDownloadUrlAsync(default, default, default);
    }

    [Fact]
    public async Task Review_DeletedReceipt_OmitsMetadataButPreservesAmount()
    {
        user.EmployeeId.Returns(claim.EmployeeId);
        permissions.HasAsync(userId, "expense.my.claims", Arg.Any<CancellationToken>()).Returns(true);
        documents.GetActiveAsync(claim.TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, AttachedDocumentInfo>());
        var result = await Review();
        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Single().ReceiptAttachments.ShouldBeEmpty();
        result.Value.TotalAmount.ShouldBe(120m);
    }

    [Fact]
    public async Task AdminList_EmployeeOutsideScope_IsFilteredBeforeReceiptMetadataLookup()
    {
        var outside = ExpenseClaim.Create(claim.TenantId, Guid.NewGuid(), null);
        var outsideDocumentId = Guid.NewGuid();
        outside.AddItem(Guid.NewGuid(), 500m, DateOnly.FromDateTime(DateTime.UtcNow), null, "TRY", outsideDocumentId.ToString(), null);
        claims.ListAsync(null, Arg.Any<CancellationToken>()).Returns([claim, outside]);
        scope.GetVisibleEmployeeIdsAsync("expense.claims.view", Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { claim.EmployeeId });
        var result = await new ListExpenseClaimsHandler(claims, categories, documents, tenant, scope).Handle(new(null), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().Id.ShouldBe(claim.Id);
        await documents.DidNotReceive().GetActiveAsync(claim.TenantId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(outsideDocumentId)), Arg.Any<CancellationToken>());
    }
}
