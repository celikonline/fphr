using FluentValidation;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Expense.Contracts.Events;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Application.ExpenseClaims;

/// <summary>Workflow RequestType this module submits under (see Sense.Modules.Workflow.Contracts.IWorkflowService).</summary>
public static class ExpenseRequestTypes
{
    public const string Claim = "ExpenseClaim";
    public const string Advance = "Advance";
}

[RequiresPermission("expense.my.claims")]
public sealed record CreateExpenseClaimCommand(string? Description) : ICommand<Guid>;

public sealed class CreateExpenseClaimHandler(ITenantContext tenant, ICurrentUser currentUser, IExpenseClaimRepository claims)
    : ICommandHandler<CreateExpenseClaimCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateExpenseClaimCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Task.FromResult(Result.Failure<Guid>(Error.NotFound(ExpenseErrors.NoEmployeeForUser)));
        }

        var claim = ExpenseClaim.Create(tenant.TenantId, employeeId, command.Description);
        claims.Add(claim);
        return Task.FromResult(Result.Success(claim.Id));
    }
}

[RequiresPermission("expense.my.claims")]
public sealed record AddExpenseItemCommand(
    Guid ClaimId, Guid CategoryId, decimal Amount, DateOnly ReceiptDate, decimal? TaxRate, string Currency, string? ReceiptFileId, string? Description,
    IReadOnlyList<string>? ReceiptFileIds = null)
    : ICommand<Guid>;

public sealed class AddExpenseItemValidator : AbstractValidator<AddExpenseItemCommand>
{
    public AddExpenseItemValidator()
    {
        RuleFor(x => x.ClaimId).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 100).When(x => x.TaxRate is not null);
    }
}

public sealed class AddExpenseItemHandler(
    ITenantContext tenant, ICurrentUser currentUser, IExpenseClaimRepository claims, IExpenseCategoryRepository categories,
    IDocumentAttachmentLookup documents, TimeProvider clock)
    : ICommandHandler<AddExpenseItemCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddExpenseItemCommand command, CancellationToken cancellationToken)
    {
        var existing = await claims.GetByIdAsync(command.ClaimId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.EmployeeId != currentUser.EmployeeId)
        {
            return Error.Forbidden(ExpenseErrors.NotOwner);
        }

        if (ReceiptDates.IsInFuture(command.ReceiptDate, clock))
        {
            return Error.Validation(ExpenseDomainErrors.ItemReceiptDateInFuture);
        }

        var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        if (!category.Value.IsActive)
        {
            return Error.Validation(ExpenseErrors.CategoryInactive);
        }

        var receipt = await ReceiptDocuments.ResolveManyAsync(documents, tenant.TenantId, existing.Value.EmployeeId, currentUser.UserId,
            command.ReceiptFileIds ?? (string.IsNullOrWhiteSpace(command.ReceiptFileId) ? [] : [command.ReceiptFileId]), cancellationToken);
        if (receipt.IsFailure)
        {
            return receipt.Error;
        }

        var added = existing.Value.AddItem(command.CategoryId, command.Amount, command.ReceiptDate, command.TaxRate, command.Currency, null, command.Description, receipt.Value);
        return added.IsFailure ? added.Error : added.Value.Id;
    }
}

[RequiresPermission("expense.my.claims")]
public sealed record UpdateExpenseItemCommand(
    Guid ClaimId, Guid ItemId, Guid CategoryId, decimal Amount, DateOnly ReceiptDate, decimal? TaxRate, string Currency, string? ReceiptFileId, string? Description,
    IReadOnlyList<string>? ReceiptFileIds = null)
    : ICommand;

public sealed class UpdateExpenseItemValidator : AbstractValidator<UpdateExpenseItemCommand>
{
    public UpdateExpenseItemValidator()
    {
        RuleFor(x => x.ClaimId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 100).When(x => x.TaxRate is not null);
    }
}

public sealed class UpdateExpenseItemHandler(
    ITenantContext tenant, ICurrentUser currentUser, IExpenseClaimRepository claims, IDocumentAttachmentLookup documents, TimeProvider clock)
    : ICommandHandler<UpdateExpenseItemCommand>
{
    public async Task<Result> Handle(UpdateExpenseItemCommand command, CancellationToken cancellationToken)
    {
        var existing = await claims.GetByIdAsync(command.ClaimId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.EmployeeId != currentUser.EmployeeId)
        {
            return Error.Forbidden(ExpenseErrors.NotOwner);
        }

        if (ReceiptDates.IsInFuture(command.ReceiptDate, clock))
        {
            return Error.Validation(ExpenseDomainErrors.ItemReceiptDateInFuture);
        }

        var item = existing.Value.Items.FirstOrDefault(i => i.Id == command.ItemId);
        if (item is null)
        {
            return Error.NotFound(ExpenseDomainErrors.ItemNotFound);
        }

        // Previously stored non-GUID file keys stay valid for payroll when editing unrelated fields.
        if (command.ReceiptFileIds is null && item.ReceiptFileId is { } legacy
            && !Guid.TryParse(legacy, out _) && string.Equals(legacy, command.ReceiptFileId?.Trim(), StringComparison.Ordinal))
        {
            return existing.Value.UpdateItem(command.ItemId, command.CategoryId, command.Amount, command.ReceiptDate,
                command.TaxRate, command.Currency, legacy, command.Description);
        }

        var receipt = await ReceiptDocuments.ResolveManyAsync(documents, tenant.TenantId, existing.Value.EmployeeId, currentUser.UserId,
            command.ReceiptFileIds ?? (string.IsNullOrWhiteSpace(command.ReceiptFileId) ? [] : [command.ReceiptFileId]), cancellationToken);
        if (receipt.IsFailure)
        {
            return receipt.Error;
        }

        return existing.Value.UpdateItem(
            command.ItemId, command.CategoryId, command.Amount, command.ReceiptDate, command.TaxRate, command.Currency, null, command.Description, receipt.Value);
    }
}

[RequiresPermission("expense.my.claims")]
public sealed record RemoveExpenseItemCommand(Guid ClaimId, Guid ItemId) : ICommand;

public sealed class RemoveExpenseItemHandler(ICurrentUser currentUser, IExpenseClaimRepository claims) : ICommandHandler<RemoveExpenseItemCommand>
{
    public async Task<Result> Handle(RemoveExpenseItemCommand command, CancellationToken cancellationToken)
    {
        var existing = await claims.GetByIdAsync(command.ClaimId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.EmployeeId != currentUser.EmployeeId)
        {
            return Error.Forbidden(ExpenseErrors.NotOwner);
        }

        return existing.Value.RemoveItem(command.ItemId);
    }
}

/// <summary>Submit -> Workflow starts the "ExpenseClaim" approval process; a Submitted event lands on this module's own outbox.</summary>
[RequiresPermission("expense.my.claims")]
public sealed record SubmitExpenseClaimCommand(Guid ClaimId) : ICommand;

public sealed class SubmitExpenseClaimHandler(
    ICurrentUser currentUser, TimeProvider clock, IExpenseClaimRepository claims, IWorkflowService workflow, IIntegrationEventOutbox outbox)
    : ICommandHandler<SubmitExpenseClaimCommand>
{
    public async Task<Result> Handle(SubmitExpenseClaimCommand command, CancellationToken cancellationToken)
    {
        var existing = await claims.GetByIdAsync(command.ClaimId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var claim = existing.Value;
        if (claim.EmployeeId != currentUser.EmployeeId)
        {
            return Error.Forbidden(ExpenseErrors.NotOwner);
        }

        var submitted = claim.Submit();
        if (submitted.IsFailure)
        {
            return submitted;
        }

        var approval = await workflow.StartAsync(
            ExpenseRequestTypes.Claim, claim.Id, claim.EmployeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        claim.AttachApproval(approval.Value, clock.GetUtcNow().UtcDateTime);
        outbox.Enqueue(new ExpenseClaimSubmitted(claim.TenantId, claim.Id, claim.EmployeeId, claim.TotalAmount, claim.Items[0].Currency));
        return Result.Success();
    }
}

/// <summary>HR/Finance marks an Approved claim as paid (e.g. after a bank transfer). Status must already be Approved.</summary>
[RequiresPermission("expense.claims.pay")]
public sealed record PayExpenseClaimCommand(Guid ClaimId, string? PaymentNote) : ICommand;

public sealed class PayExpenseClaimValidator : AbstractValidator<PayExpenseClaimCommand>
{
    public PayExpenseClaimValidator()
    {
        RuleFor(x => x.ClaimId).NotEmpty();
        RuleFor(x => x.PaymentNote).MaximumLength(ExpenseLimits.DescriptionMaxLength);
    }
}

public sealed class PayExpenseClaimHandler(ICurrentUser currentUser, TimeProvider clock, IExpenseClaimRepository claims, IIntegrationEventOutbox outbox)
    : ICommandHandler<PayExpenseClaimCommand>
{
    public async Task<Result> Handle(PayExpenseClaimCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } payerEmployeeId)
        {
            return Error.NotFound(ExpenseErrors.NoEmployeeForUser);
        }

        var existing = await claims.GetByIdAsync(command.ClaimId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var claim = existing.Value;
        var paid = claim.MarkPaid(clock.GetUtcNow().UtcDateTime, payerEmployeeId, command.PaymentNote);
        if (paid.IsFailure)
        {
            return paid;
        }

        outbox.Enqueue(new ExpenseClaimPaid(claim.TenantId, claim.Id, claim.EmployeeId, claim.TotalAmount));
        return Result.Success();
    }
}

/// <summary>
/// Payroll pays an item with a non-null ReceiptFileId as tax-free reimbursement (see IApprovedExpensePayoutReader), so the
/// id must be a verifiable receipt: an Active document of this tenant that the claim's employee owns - either filed to
/// them (<see cref="AttachedDocumentInfo.EmployeeId"/>) or their own self-service upload (no employee, uploaded by the
/// acting user). A document the caller uploaded for a different employee doesn't count. Unknown/deleted/foreign ids
/// all collapse into the same error so document existence isn't leaked (same rule as Leave's attachment linker).
/// </summary>
internal static class ReceiptDocuments
{
    private static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/pjpeg", "image/png", "image/gif", "image/webp", "image/heic", "image/heif",
    };

    public static async Task<Result<IReadOnlyCollection<Guid>>> ResolveManyAsync(
        IDocumentAttachmentLookup documents, Guid tenantId, Guid claimEmployeeId, Guid? actingUserId,
        IReadOnlyList<string> receiptFileIds, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var value in receiptFileIds)
        {
            if (!Guid.TryParse(value?.Trim(), out var id) || id == Guid.Empty)
            {
                return Error.Validation(ExpenseErrors.ReceiptNotFound);
            }

            if (!ids.Contains(id)) ids.Add(id);
        }

        if (ids.Count == 0) return Result.Success<IReadOnlyCollection<Guid>>(ids);
        var active = await documents.GetActiveAsync(tenantId, ids, ct);
        foreach (var id in ids)
        {
            if (!active.TryGetValue(id, out var info) || !IsOwnedBy(info, claimEmployeeId, actingUserId))
            {
                return Error.Validation(ExpenseErrors.ReceiptNotFound);
            }

            if (!ContentTypes.Contains(info.ContentType.Split(';')[0].Trim()) || info.Size <= 0 || info.Size > 100L * 1024 * 1024)
            {
                return Error.Validation(ExpenseErrors.ReceiptTypeNotAllowed);
            }
        }

        return Result.Success<IReadOnlyCollection<Guid>>(ids);
    }

    /// <summary>Returns the canonical document id to store, or null when no receipt was given.</summary>
    public static async Task<Result<string?>> ResolveAsync(
        IDocumentAttachmentLookup documents, Guid tenantId, Guid claimEmployeeId, Guid? actingUserId, string? receiptFileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(receiptFileId))
        {
            return Result.Success<string?>(null);
        }

        if (!Guid.TryParse(receiptFileId.Trim(), out var documentId))
        {
            return Error.Validation(ExpenseErrors.ReceiptNotFound);
        }

        var resolved = await ResolveManyAsync(documents, tenantId, claimEmployeeId, actingUserId, [receiptFileId], ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        return Result.Success<string?>(documentId.ToString());
    }

    private static bool IsOwnedBy(AttachedDocumentInfo info, Guid claimEmployeeId, Guid? actingUserId) =>
        info.EmployeeId == claimEmployeeId || (info.EmployeeId is null && actingUserId is { } userId && info.UploadedBy == userId);
}

internal static class ReceiptDates
{
    /// <summary>A receipt dated after tomorrow (today + <see cref="ExpenseLimits.MaxReceiptDateDaysAhead"/>) is a typo/fraud signal.</summary>
    public static bool IsInFuture(DateOnly receiptDate, TimeProvider clock) =>
        receiptDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(ExpenseLimits.MaxReceiptDateDaysAhead);
}
