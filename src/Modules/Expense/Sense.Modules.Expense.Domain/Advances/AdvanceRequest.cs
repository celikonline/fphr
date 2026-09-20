using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Domain.Advances;

public enum AdvanceRequestStatus
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    Paid = 3,
}

/// <summary>
/// An advance-payment request ("Talep > Avans"). Unlike ExpenseClaim there is no Draft phase - per the source
/// walkthrough (part1: Tutar -> Kaydet) Create already submits; only Amount is strictly required, the rest default.
/// </summary>
public sealed class AdvanceRequest : TenantAggregateRoot<Guid>, IAuditLogged
{
    private AdvanceRequest()
    {
    }

    private AdvanceRequest(
        Guid id, Guid tenantId, Guid employeeId, decimal amount, string currency, DateOnly requestedDate,
        bool isInstallmentRequested, int? installmentCount, string? description)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Amount = amount;
        Currency = currency;
        RequestedDate = requestedDate;
        IsInstallmentRequested = isInstallmentRequested;
        InstallmentCount = installmentCount;
        Description = description;
        Status = AdvanceRequestStatus.Submitted;
    }

    public Guid EmployeeId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateOnly RequestedDate { get; private set; }

    public bool IsInstallmentRequested { get; private set; }

    public int? InstallmentCount { get; private set; }

    public string? Description { get; private set; }

    public AdvanceRequestStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public DateTime? PaidAt { get; private set; }

    public Guid? PaidByEmployeeId { get; private set; }

    public string? PaymentNote { get; private set; }

    public static Result<AdvanceRequest> Submit(
        Guid tenantId, Guid employeeId, decimal amount, string currency, DateOnly requestedDate,
        bool isInstallmentRequested, int? installmentCount, string? description)
    {
        if (amount <= 0)
        {
            return Error.Validation(ExpenseDomainErrors.AdvanceAmountInvalid);
        }

        if (amount > ExpenseLimits.MaxAmount)
        {
            return Error.Validation(ExpenseDomainErrors.AdvanceAmountTooLarge);
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != ExpenseLimits.CurrencyLength)
        {
            return Error.Validation(ExpenseDomainErrors.ItemCurrencyRequired);
        }

        if (!ExpenseLimits.AllowedCurrencies.Contains(currency.Trim().ToUpperInvariant()))
        {
            return Error.Validation(ExpenseDomainErrors.ItemCurrencyUnsupported);
        }

        if (isInstallmentRequested && installmentCount is not (> 0 and <= ExpenseLimits.MaxInstallments))
        {
            return Error.Validation(ExpenseDomainErrors.AdvanceInstallmentCountInvalid);
        }

        return new AdvanceRequest(
            Guid.CreateVersion7(), tenantId, employeeId, amount, currency.Trim().ToUpperInvariant(), requestedDate,
            isInstallmentRequested, isInstallmentRequested ? installmentCount : null,
            string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), ExpenseLimits.DescriptionMaxLength));
    }

    public void AttachApproval(Guid approvalRequestId) => ApprovalRequestId = approvalRequestId;

    public Result Approve(Guid approvalRequestId, DateTime whenUtc)
    {
        if (Status != AdvanceRequestStatus.Submitted || ApprovalRequestId != approvalRequestId)
        {
            return Error.Rule(ExpenseDomainErrors.AdvanceNotSubmitted);
        }

        Status = AdvanceRequestStatus.Approved;
        DecidedAt = whenUtc;
        return Result.Success();
    }

    public Result Reject(Guid approvalRequestId, DateTime whenUtc)
    {
        if (Status != AdvanceRequestStatus.Submitted || ApprovalRequestId != approvalRequestId)
        {
            return Error.Rule(ExpenseDomainErrors.AdvanceNotSubmitted);
        }

        Status = AdvanceRequestStatus.Rejected;
        DecidedAt = whenUtc;
        return Result.Success();
    }

    public Result MarkPaid(DateTime whenUtc, Guid paidByEmployeeId, string? paymentNote)
    {
        if (Status != AdvanceRequestStatus.Approved)
        {
            return Error.Rule(ExpenseDomainErrors.AdvanceAlreadyPaid);
        }

        Status = AdvanceRequestStatus.Paid;
        PaidAt = whenUtc;
        PaidByEmployeeId = paidByEmployeeId;
        PaymentNote = string.IsNullOrWhiteSpace(paymentNote) ? null : Guard.MaxLength(paymentNote.Trim(), ExpenseLimits.DescriptionMaxLength);
        return Result.Success();
    }
}
