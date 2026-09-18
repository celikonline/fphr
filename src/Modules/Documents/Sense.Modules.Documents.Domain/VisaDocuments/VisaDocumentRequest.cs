using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain.VisaDocuments;

/// <summary>
/// Status of a visa document request in its lifecycle.
/// </summary>
public enum VisaDocumentRequestStatus
{
    /// <summary>Request created, pending submission for approval.</summary>
    Pending = 0,

    /// <summary>Request submitted and awaiting approval from designated approver.</summary>
    AwaitingApproval = 1,

    /// <summary>Request approved; employee may upload required visa documents.</summary>
    Approved = 2,

    /// <summary>Request rejected by approver; see RejectionReason for details.</summary>
    Rejected = 3,

    /// <summary>Request completed; visa documents have been delivered to employee.</summary>
    Completed = 4,
}

/// <summary>
/// VisaDocumentRequest aggregate root: represents an employee's request for visa document processing.
/// Tenant isolation: each request belongs to exactly one tenant.
/// </summary>
public sealed class VisaDocumentRequest : TenantAggregateRoot<Guid>, IAuditLogged
{
    private VisaDocumentRequest()
    {
    }

    private VisaDocumentRequest(
        Guid id,
        Guid tenantId,
        Guid employeeId,
        Guid? visaDocumentProcessId,
        DateTime travelStart,
        DateTime travelEnd,
        string countryCode,
        string visaType,
        string description) : base(id, tenantId)
    {
        EmployeeId = employeeId;
        VisaDocumentProcessId = visaDocumentProcessId;
        TravelStart = travelStart;
        TravelEnd = travelEnd;
        CountryCode = countryCode;
        VisaType = visaType;
        Description = description;
        Status = VisaDocumentRequestStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Employee who initiated the visa document request.</summary>
    public Guid EmployeeId { get; private set; }

    /// <summary>Selected visa document process (optional in v1, may be null if no process selected).</summary>
    public Guid? VisaDocumentProcessId { get; private set; }

    /// <summary>Start date of intended travel.</summary>
    public DateTime TravelStart { get; private set; }

    /// <summary>End date of intended travel.</summary>
    public DateTime TravelEnd { get; private set; }

    /// <summary>Destination country code (e.g., "TR", "DE", "US").</summary>
    public string CountryCode { get; private set; } = string.Empty;

    /// <summary>Type of visa requested (e.g., "Business", "Tourist", "Work", "Student", "Transit").</summary>
    public string VisaType { get; private set; } = string.Empty;

    /// <summary>Free-form description of the visa request (e.g., "Business meeting in Istanbul").</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Current status in the request lifecycle (Pending, AwaitingApproval, Approved, Rejected, Completed).</summary>
    public VisaDocumentRequestStatus Status { get; private set; }

    /// <summary>ID of the workflow ApprovalRequest if submitted for approval; null until SubmitForApproval() is called.</summary>
    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>Reason for rejection, if Status is Rejected.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>User ID who rejected the request, if applicable.</summary>
    public Guid? RejectedBy { get; private set; }

    /// <summary>Timestamp of rejection, if applicable.</summary>
    public DateTime? RejectedAt { get; private set; }

    /// <summary>Timestamp of request creation.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Timestamp of when request was approved (if approved).</summary>
    public DateTime? ApprovedAt { get; private set; }

    /// <summary>Timestamp of when documents were delivered to employee (if completed).</summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Factory method to create a new visa document request.
    /// </summary>
    public static Result<VisaDocumentRequest> Create(
        Guid tenantId,
        Guid employeeId,
        Guid? visaDocumentProcessId,
        DateTime travelStart,
        DateTime travelEnd,
        string countryCode,
        string visaType,
        string description)
    {
        // Validate travel dates: end must be >= start
        if (travelEnd < travelStart)
        {
            return Result.Failure<VisaDocumentRequest>(
                new Error(VisaDomainErrors.VisaDocumentRequestInvalidTravelDates, "Travel end date must be after or equal to travel start date"));
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return Result.Failure<VisaDocumentRequest>(
                new Error(VisaDomainErrors.VisaDocumentRequestCountryCodeRequired, "Country code is required"));
        }

        if (string.IsNullOrWhiteSpace(visaType))
        {
            return Result.Failure<VisaDocumentRequest>(
                new Error(VisaDomainErrors.VisaDocumentRequestVisaTypeRequired, "Visa type is required"));
        }

        var request = new VisaDocumentRequest(
            id: Guid.NewGuid(),
            tenantId: tenantId,
            employeeId: employeeId,
            visaDocumentProcessId: visaDocumentProcessId,
            travelStart: travelStart,
            travelEnd: travelEnd,
            countryCode: countryCode,
            visaType: visaType,
            description: description);

        return Result.Success(request);
    }

    /// <summary>
    /// Submit the request for approval. Transitions status to AwaitingApproval and records the approval request ID.
    /// </summary>
    public void SubmitForApproval(Guid approvalRequestId)
    {
        if (Status != VisaDocumentRequestStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot submit request for approval when status is {Status}");
        }

        Status = VisaDocumentRequestStatus.AwaitingApproval;
        ApprovalRequestId = approvalRequestId;
    }

    /// <summary>
    /// Approve the request. Transitions status to Approved and records the approval timestamp.
    /// </summary>
    public void Approve()
    {
        if (Status != VisaDocumentRequestStatus.AwaitingApproval)
        {
            throw new InvalidOperationException($"Cannot approve request when status is {Status}");
        }

        Status = VisaDocumentRequestStatus.Approved;
        ApprovedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reject the request with a reason. Transitions status to Rejected and records the rejection details.
    /// </summary>
    public void Reject(string rejectionReason, Guid? rejectedBy = null)
    {
        if (Status != VisaDocumentRequestStatus.AwaitingApproval)
        {
            throw new InvalidOperationException($"Cannot reject request when status is {Status}");
        }

        Status = VisaDocumentRequestStatus.Rejected;
        RejectionReason = rejectionReason;
        RejectedBy = rejectedBy;
        RejectedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark the request as completed. Transitions status to Completed and records the completion timestamp.
    /// </summary>
    public void Complete()
    {
        if (Status != VisaDocumentRequestStatus.Approved)
        {
            throw new InvalidOperationException($"Cannot complete request when status is {Status}");
        }

        Status = VisaDocumentRequestStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }
}
