using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Expense.Contracts.Events;

/// <summary>Published when an employee submits a multi-receipt expense claim for approval.</summary>
public sealed record ExpenseClaimSubmitted(Guid TenantId, Guid ClaimId, Guid EmployeeId, decimal TotalAmount, string Currency)
    : IntegrationEvent(TenantId);

/// <summary>Published when Workflow approves an expense claim's approval request.</summary>
public sealed record ExpenseClaimApproved(Guid TenantId, Guid ClaimId, Guid EmployeeId) : IntegrationEvent(TenantId);

/// <summary>Published when Workflow rejects an expense claim's approval request.</summary>
public sealed record ExpenseClaimRejected(Guid TenantId, Guid ClaimId, Guid EmployeeId) : IntegrationEvent(TenantId);

/// <summary>Published when an employee submits an advance-payment request.</summary>
public sealed record AdvanceRequestSubmitted(Guid TenantId, Guid AdvanceRequestId, Guid EmployeeId, decimal Amount, string Currency)
    : IntegrationEvent(TenantId);

/// <summary>Published when Workflow approves an advance request's approval request.</summary>
public sealed record AdvanceRequestApproved(Guid TenantId, Guid AdvanceRequestId, Guid EmployeeId) : IntegrationEvent(TenantId);

/// <summary>Published when Workflow rejects an advance request's approval request.</summary>
public sealed record AdvanceRequestRejected(Guid TenantId, Guid AdvanceRequestId, Guid EmployeeId) : IntegrationEvent(TenantId);
