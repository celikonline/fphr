using Sense.Modules.Documents.Contracts;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.ExpenseClaims;

// These read routes have alternative record-specific grants; RequiresPermission attributes are conjunctive.
public sealed record ReviewExpenseClaimQuery(Guid ClaimId) : IQuery<ExpenseClaimDto>;
public sealed record DownloadExpenseReceiptQuery(Guid ClaimId, Guid ItemId, Guid DocumentId) : IQuery<AttachedDocumentDownloadUrl>;

public sealed class ExpenseClaimReadAccess(ITenantContext tenant, ICurrentUser user, IPermissionService permissions,
    IDataScopeService scope, IApprovalRequestReadAccess workflow)
{
    public async Task<bool> CanReadAsync(ExpenseClaim claim, CancellationToken ct)
    {
        if (!user.IsAuthenticated || claim.TenantId != tenant.TenantId || user.UserId is not { } userId) return false;
        if (claim.EmployeeId == user.EmployeeId && await permissions.HasAsync(userId, "expense.my.claims", ct)) return true;
        if (await permissions.HasAsync(userId, "expense.claims.view", ct)
            && await scope.CanAccessEmployeeAsync("expense.claims.view", claim.EmployeeId, ct)) return true;
        return claim.ApprovalRequestId is { } requestId
            && await workflow.CanReadAsync(tenant.TenantId, requestId, ExpenseRequestTypes.Claim, claim.Id, user.EmployeeId, userId, ct);
    }
}

public sealed class ReviewExpenseClaimHandler(IExpenseClaimRepository claims, IExpenseCategoryRepository categories,
    IDocumentAttachmentLookup documents, ITenantContext tenant, ExpenseClaimReadAccess access)
    : IQueryHandler<ReviewExpenseClaimQuery, ExpenseClaimDto>
{
    public async Task<Result<ExpenseClaimDto>> Handle(ReviewExpenseClaimQuery query, CancellationToken cancellationToken)
    {
        var loaded = await claims.GetByIdAsync(query.ClaimId, cancellationToken);
        if (loaded.IsFailure || !await access.CanReadAsync(loaded.Value, cancellationToken))
            return Error.NotFound(ExpenseDomainErrors.ClaimNotFound);
        var limits = await ExpenseCategoryLimitsHelper.CategoryLimitsAsync(categories, cancellationToken);
        return (await ExpenseClaimMapper.ToDtosAsync([loaded.Value], limits, documents, tenant.TenantId, cancellationToken)).Single();
    }
}

public sealed class DownloadExpenseReceiptHandler(IExpenseClaimRepository claims, IDocumentAttachmentLookup documents,
    ITenantContext tenant, ExpenseClaimReadAccess access) : IQueryHandler<DownloadExpenseReceiptQuery, AttachedDocumentDownloadUrl>
{
    public async Task<Result<AttachedDocumentDownloadUrl>> Handle(DownloadExpenseReceiptQuery query, CancellationToken cancellationToken)
    {
        var loaded = await claims.GetByIdAsync(query.ClaimId, cancellationToken);
        if (loaded.IsFailure || !await access.CanReadAsync(loaded.Value, cancellationToken))
            return Error.NotFound(ExpenseDomainErrors.ClaimNotFound);
        var item = loaded.Value.Items.FirstOrDefault(i => i.Id == query.ItemId);
        if (item is null || !ExpenseClaimMapper.DocumentIds(item).Contains(query.DocumentId))
            return Error.NotFound(ExpenseDomainErrors.ClaimNotFound);
        var download = await documents.GetDownloadUrlAsync(tenant.TenantId, query.DocumentId, cancellationToken);
        return download is null ? Error.NotFound(ExpenseDomainErrors.ClaimNotFound) : download;
    }
}
