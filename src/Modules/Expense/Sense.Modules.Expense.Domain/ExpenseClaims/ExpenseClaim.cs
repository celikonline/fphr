using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Domain.ExpenseClaims;

public enum ExpenseClaimStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4,
}

/// <summary>
/// A multi-receipt expense claim ("Talep > Harcama"): a Draft accumulates one or more <see cref="ExpenseItem"/> rows,
/// then Submit hands it to Workflow (IWorkflowService, RequestType "ExpenseClaim") for approval. TotalAmount is
/// always server-recomputed from the item list, never accepted from the client.
/// </summary>
public sealed class ExpenseClaim : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<ExpenseItem> _items = [];

    private ExpenseClaim()
    {
    }

    private ExpenseClaim(Guid id, Guid tenantId, Guid employeeId, string? description)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Description = Trim(description);
        Status = ExpenseClaimStatus.Draft;
    }

    public Guid EmployeeId { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Sum of every item's AmountInBaseCurrency-equivalent; MVP scope keeps a single currency per claim (no FX conversion yet), see ExpenseItem.Currency.</summary>
    public decimal TotalAmount { get; private set; }

    public ExpenseClaimStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public DateTime? SubmittedAt { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public DateTime? PaidAt { get; private set; }

    public Guid? PaidByEmployeeId { get; private set; }

    public string? PaymentNote { get; private set; }

    public IReadOnlyList<ExpenseItem> Items => _items.AsReadOnly();

    public static ExpenseClaim Create(Guid tenantId, Guid employeeId, string? description) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, description);

    public Result<ExpenseItem> AddItem(
        Guid categoryId, decimal amount, DateOnly receiptDate, decimal? taxRate, string currency, string? receiptFileId, string? description)
    {
        if (Status != ExpenseClaimStatus.Draft)
        {
            return Error.Rule(ExpenseDomainErrors.ClaimNotDraft);
        }

        var validated = ExpenseItem.Create(Id, categoryId, amount, receiptDate, taxRate, currency, receiptFileId, description);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        _items.Add(validated.Value);
        RecalculateTotal();
        return validated.Value;
    }

    public Result UpdateItem(
        Guid itemId, Guid categoryId, decimal amount, DateOnly receiptDate, decimal? taxRate, string currency, string? receiptFileId, string? description)
    {
        if (Status != ExpenseClaimStatus.Draft)
        {
            return Error.Rule(ExpenseDomainErrors.ClaimNotDraft);
        }

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return Error.NotFound(ExpenseDomainErrors.ItemNotFound);
        }

        var updated = item.Update(categoryId, amount, receiptDate, taxRate, currency, receiptFileId, description);
        if (updated.IsFailure)
        {
            return updated;
        }

        RecalculateTotal();
        return Result.Success();
    }

    public Result RemoveItem(Guid itemId)
    {
        if (Status != ExpenseClaimStatus.Draft)
        {
            return Error.Rule(ExpenseDomainErrors.ClaimNotDraft);
        }

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return Error.NotFound(ExpenseDomainErrors.ItemNotFound);
        }

        _items.Remove(item);
        RecalculateTotal();
        return Result.Success();
    }

    /// <summary>Draft -> Submitted; the caller (command handler) starts the Workflow approval and calls AttachApproval.</summary>
    public Result Submit()
    {
        if (Status != ExpenseClaimStatus.Draft)
        {
            return Error.Rule(ExpenseDomainErrors.ClaimNotDraft);
        }

        if (_items.Count == 0)
        {
            return Error.Validation(ExpenseDomainErrors.ClaimNoItems);
        }

        RecalculateTotal();
        Status = ExpenseClaimStatus.Submitted;
        return Result.Success();
    }

    public void AttachApproval(Guid approvalRequestId, DateTime submittedAtUtc)
    {
        ApprovalRequestId = approvalRequestId;
        SubmittedAt = submittedAtUtc;
    }

    public Result Approve(Guid approvalRequestId, DateTime whenUtc)
    {
        if (Status != ExpenseClaimStatus.Submitted || ApprovalRequestId != approvalRequestId)
        {
            return Error.Rule(ExpenseDomainErrors.ClaimNotSubmitted);
        }

        Status = ExpenseClaimStatus.Approved;
        DecidedAt = whenUtc;
        return Result.Success();
    }

    public Result Reject(Guid approvalRequestId, DateTime whenUtc)
    {
        if (Status != ExpenseClaimStatus.Submitted || ApprovalRequestId != approvalRequestId)
        {
            return Error.Rule(ExpenseDomainErrors.ClaimNotSubmitted);
        }

        Status = ExpenseClaimStatus.Rejected;
        DecidedAt = whenUtc;
        return Result.Success();
    }

    public Result MarkPaid(DateTime whenUtc, Guid paidByEmployeeId, string? paymentNote)
    {
        if (Status != ExpenseClaimStatus.Approved)
        {
            return Error.Rule(ExpenseDomainErrors.ClaimNotApproved);
        }

        Status = ExpenseClaimStatus.Paid;
        PaidAt = whenUtc;
        PaidByEmployeeId = paidByEmployeeId;
        PaymentNote = Trim(paymentNote);
        return Result.Success();
    }

    private void RecalculateTotal() => TotalAmount = _items.Sum(i => i.Amount);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), ExpenseLimits.DescriptionMaxLength);
}

/// <summary>One receipt line of an <see cref="ExpenseClaim"/>. Owns its own Guid id so it can be addressed by PUT/DELETE .../items/{itemId}.</summary>
public sealed class ExpenseItem : Entity<Guid>
{
    private ExpenseItem()
    {
    }

    private ExpenseItem(
        Guid id, Guid claimId, Guid categoryId, decimal amount, DateOnly receiptDate, decimal? taxRate, decimal taxAmount,
        string currency, string? receiptFileId, string? description)
        : base(id)
    {
        ClaimId = claimId;
        CategoryId = categoryId;
        Amount = amount;
        ReceiptDate = receiptDate;
        TaxRate = taxRate;
        TaxAmount = taxAmount;
        Currency = currency;
        ReceiptFileId = receiptFileId;
        Description = description;
    }

    public Guid ClaimId { get; private set; }

    public Guid CategoryId { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly ReceiptDate { get; private set; }

    /// <summary>VAT rate as a percentage (e.g. 20 for 20%); null = not applicable/unknown.</summary>
    public decimal? TaxRate { get; private set; }

    /// <summary>Computed assuming Amount is tax-inclusive: TaxAmount = Amount - Amount / (1 + TaxRate / 100).</summary>
    public decimal TaxAmount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public string? ReceiptFileId { get; private set; }

    public string? Description { get; private set; }

    internal static Result<ExpenseItem> Create(
        Guid claimId, Guid categoryId, decimal amount, DateOnly receiptDate, decimal? taxRate, string currency, string? receiptFileId, string? description)
    {
        var validated = Validate(amount, taxRate, currency);
        if (validated is not null)
        {
            return validated;
        }

        return new ExpenseItem(
            Guid.CreateVersion7(), claimId, categoryId, amount, receiptDate, taxRate, CalculateTaxAmount(amount, taxRate),
            currency.Trim().ToUpperInvariant(), Trim(receiptFileId, ExpenseLimits.FileKeyMaxLength), Trim(description, ExpenseLimits.DescriptionMaxLength));
    }

    internal Result Update(
        Guid categoryId, decimal amount, DateOnly receiptDate, decimal? taxRate, string currency, string? receiptFileId, string? description)
    {
        var validated = Validate(amount, taxRate, currency);
        if (validated is not null)
        {
            return validated;
        }

        CategoryId = categoryId;
        Amount = amount;
        ReceiptDate = receiptDate;
        TaxRate = taxRate;
        TaxAmount = CalculateTaxAmount(amount, taxRate);
        Currency = currency.Trim().ToUpperInvariant();
        ReceiptFileId = Trim(receiptFileId, ExpenseLimits.FileKeyMaxLength);
        Description = Trim(description, ExpenseLimits.DescriptionMaxLength);
        return Result.Success();
    }

    private static Error? Validate(decimal amount, decimal? taxRate, string currency)
    {
        if (amount <= 0)
        {
            return Error.Validation(ExpenseDomainErrors.ItemAmountInvalid);
        }

        if (taxRate is < 0 or > 100)
        {
            return Error.Validation(ExpenseDomainErrors.ItemTaxRateInvalid);
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != ExpenseLimits.CurrencyLength)
        {
            return Error.Validation(ExpenseDomainErrors.ItemCurrencyRequired);
        }

        return null;
    }

    private static decimal CalculateTaxAmount(decimal amount, decimal? taxRate) =>
        taxRate is not { } rate || rate == 0 ? 0m : Math.Round(amount - amount / (1 + rate / 100m), 2, MidpointRounding.AwayFromZero);

    private static string? Trim(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}
