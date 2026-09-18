using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Workflow;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>
/// Command to submit a visa document request for approval.
/// Validates approver configuration and initiates workflow approval.
/// </summary>
[RequiresPermission("documents.visa.request")]
public sealed record SubmitVisaDocumentRequestCommand(
    Guid EmployeeId,
    Guid? VisaDocumentProcessId,
    DateTime TravelStart,
    DateTime TravelEnd,
    string CountryCode,
    string VisaType,
    string Description) : ICommand<SubmitVisaDocumentRequestResponse>;

public sealed class SubmitVisaDocumentRequestValidator : AbstractValidator<SubmitVisaDocumentRequestCommand>
{
    public SubmitVisaDocumentRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.TravelStart).NotEmpty();
        RuleFor(x => x.TravelEnd).NotEmpty();
        RuleFor(x => x.CountryCode).NotEmpty().MaximumLength(VisaLimits.CountryCodeMaxLength);
        RuleFor(x => x.VisaType).NotEmpty().MaximumLength(VisaLimits.VisaTypeMaxLength);
        RuleFor(x => x.Description).MaximumLength(VisaLimits.DescriptionMaxLength);
    }
}

public sealed class SubmitVisaDocumentRequestHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IVisaDocumentRequestRepository visaRequests,
    IWorkflowService workflowService,
    ITenantSettings tenantSettings,
    ILogger<SubmitVisaDocumentRequestHandler> logger)
    : ICommandHandler<SubmitVisaDocumentRequestCommand, SubmitVisaDocumentRequestResponse>
{
    public async Task<Result<SubmitVisaDocumentRequestResponse>> Handle(
        SubmitVisaDocumentRequestCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Submitting visa document request for employee {EmployeeId} to {CountryCode}",
            command.EmployeeId,
            command.CountryCode);

        // Validate approver is configured
        var approverId = await tenantSettings.GetVisaApproverAsync(tenant.Id, cancellationToken);
        if (approverId == Guid.Empty)
        {
            logger.LogWarning("Visa approver not configured for tenant {TenantId}", tenant.Id);
            return Result.Failure<SubmitVisaDocumentRequestResponse>(
                new Error(
                    VisaDomainErrors.VisaDocumentRequestApproverNotConfigured,
                    "Visa document approver is not configured in tenant settings"));
        }

        // Create the domain request
        var requestResult = VisaDocumentRequest.Create(
            tenantId: tenant.Id,
            employeeId: command.EmployeeId,
            visaDocumentProcessId: command.VisaDocumentProcessId,
            travelStart: command.TravelStart,
            travelEnd: command.TravelEnd,
            countryCode: command.CountryCode,
            visaType: command.VisaType,
            description: command.Description);

        if (requestResult.IsFailure)
        {
            return Result.Failure<SubmitVisaDocumentRequestResponse>(requestResult.Error);
        }

        var request = requestResult.Value;

        // Initiate workflow approval via Workflow module
        var approvalResult = await workflowService.StartAsync(
            requestType: "VisaDocument",
            referenceId: request.Id,
            initiatorId: command.EmployeeId,
            approverIds: new[] { approverId },
            context: new Dictionary<string, object>
            {
                { "CountryCode", command.CountryCode },
                { "VisaType", command.VisaType },
                { "TravelStart", command.TravelStart },
                { "TravelEnd", command.TravelEnd }
            },
            cancellationToken: cancellationToken);

        if (approvalResult.IsFailure)
        {
            logger.LogError("Failed to initiate workflow approval: {Error}", approvalResult.Error.Message);
            return Result.Failure<SubmitVisaDocumentRequestResponse>(approvalResult.Error);
        }

        var approvalRequestId = approvalResult.Value.Id;
        request.SubmitForApproval(approvalRequestId);

        // Persist the request
        await visaRequests.AddAsync(request, cancellationToken);
        await visaRequests.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Visa document request {RequestId} submitted for approval (ApprovalRequestId: {ApprovalRequestId})",
            request.Id,
            approvalRequestId);

        return Result.Success(new SubmitVisaDocumentRequestResponse(
            RequestId: request.Id,
            Status: request.Status,
            ApprovalRequestId: approvalRequestId));
    }
}

/// <summary>Response DTO for visa document request submission.</summary>
public sealed record SubmitVisaDocumentRequestResponse(
    Guid RequestId,
    VisaDocumentRequestStatus Status,
    Guid ApprovalRequestId);

/// <summary>
/// Command to approve a visa document request.
/// Called by workflow approval completion handler.
/// </summary>
[RequiresPermission("documents.visa.approve")]
public sealed record ApproveVisaDocumentRequestCommand(
    Guid RequestId) : ICommand<ApproveVisaDocumentRequestResponse>;

public sealed class ApproveVisaDocumentRequestValidator : AbstractValidator<ApproveVisaDocumentRequestCommand>
{
    public ApproveVisaDocumentRequestValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
    }
}

public sealed class ApproveVisaDocumentRequestHandler(
    ITenantContext tenant,
    IVisaDocumentRequestRepository visaRequests,
    IDocumentDeliveryService documentDelivery,
    ILogger<ApproveVisaDocumentRequestHandler> logger)
    : ICommandHandler<ApproveVisaDocumentRequestCommand, ApproveVisaDocumentRequestResponse>
{
    public async Task<Result<ApproveVisaDocumentRequestResponse>> Handle(
        ApproveVisaDocumentRequestCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Approving visa document request {RequestId}", command.RequestId);

        var request = await visaRequests.GetByIdAsync(command.RequestId, cancellationToken);
        if (request == null)
        {
            return Result.Failure<ApproveVisaDocumentRequestResponse>(
                new Error(VisaDomainErrors.VisaDocumentRequestNotFound, "Visa document request not found"));
        }

        if (request.TenantId != tenant.Id)
        {
            logger.LogWarning(
                "Tenant {TenantId} attempted to access request from tenant {OwnerTenantId}",
                tenant.Id,
                request.TenantId);
            return Result.Failure<ApproveVisaDocumentRequestResponse>(
                new Error("tenant.not_authorized", "Not authorized to access this request"));
        }

        // Approve in domain
        request.Approve();

        // Deliver template documents to employee
        var deliveryResult = await documentDelivery.DeliverVisaDocumentsAsync(
            request.TenantId,
            request.EmployeeId,
            request.Id,
            cancellationToken);

        if (deliveryResult.IsFailure)
        {
            logger.LogError("Failed to deliver visa documents: {Error}", deliveryResult.Error.Message);
            return Result.Failure<ApproveVisaDocumentRequestResponse>(deliveryResult.Error);
        }

        // Mark as completed
        request.Complete();

        // Persist
        await visaRequests.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Visa document request {RequestId} approved and documents delivered to employee {EmployeeId}",
            request.Id,
            request.EmployeeId);

        return Result.Success(new ApproveVisaDocumentRequestResponse(
            RequestId: request.Id,
            Status: request.Status,
            DocumentsDelivered: deliveryResult.Value));
    }
}

public sealed record ApproveVisaDocumentRequestResponse(
    Guid RequestId,
    VisaDocumentRequestStatus Status,
    int DocumentsDelivered);

/// <summary>
/// Command to reject a visa document request.
/// Called by workflow approval rejection or by administrator.
/// </summary>
[RequiresPermission("documents.visa.approve")]
public sealed record RejectVisaDocumentRequestCommand(
    Guid RequestId,
    string RejectionReason,
    Guid? RejectedBy = null) : ICommand<RejectVisaDocumentRequestResponse>;

public sealed class RejectVisaDocumentRequestValidator : AbstractValidator<RejectVisaDocumentRequestCommand>
{
    public RejectVisaDocumentRequestValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.RejectionReason)
            .NotEmpty()
            .MaximumLength(VisaLimits.RejectionReasonMaxLength);
    }
}

public sealed class RejectVisaDocumentRequestHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IVisaDocumentRequestRepository visaRequests,
    ILogger<RejectVisaDocumentRequestHandler> logger)
    : ICommandHandler<RejectVisaDocumentRequestCommand, RejectVisaDocumentRequestResponse>
{
    public async Task<Result<RejectVisaDocumentRequestResponse>> Handle(
        RejectVisaDocumentRequestCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Rejecting visa document request {RequestId}", command.RequestId);

        var request = await visaRequests.GetByIdAsync(command.RequestId, cancellationToken);
        if (request == null)
        {
            return Result.Failure<RejectVisaDocumentRequestResponse>(
                new Error(VisaDomainErrors.VisaDocumentRequestNotFound, "Visa document request not found"));
        }

        if (request.TenantId != tenant.Id)
        {
            return Result.Failure<RejectVisaDocumentRequestResponse>(
                new Error("tenant.not_authorized", "Not authorized to access this request"));
        }

        var rejectedBy = command.RejectedBy ?? currentUser.Id;
        request.Reject(command.RejectionReason, rejectedBy);

        await visaRequests.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Visa document request {RequestId} rejected with reason: {Reason}",
            request.Id,
            command.RejectionReason);

        return Result.Success(new RejectVisaDocumentRequestResponse(
            RequestId: request.Id,
            Status: request.Status,
            RejectionReason: request.RejectionReason!));
    }
}

public sealed record RejectVisaDocumentRequestResponse(
    Guid RequestId,
    VisaDocumentRequestStatus Status,
    string RejectionReason);
