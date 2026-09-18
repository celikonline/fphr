using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>
/// Submits a visa document request and starts the Workflow approval process for it.
/// The approver is read from the tenant's visa settings; a missing approver is a hard failure
/// (the request is not created) so the employee gets an actionable error instead of a silent no-op.
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
    IVisaDocumentRequestRepository visaRequests,
    IWorkflowService workflowService,
    IVisaSettingsProvider visaSettings,
    ILogger<SubmitVisaDocumentRequestHandler> logger)
    : ICommandHandler<SubmitVisaDocumentRequestCommand, SubmitVisaDocumentRequestResponse>
{
    /// <summary>Workflow request type discriminator for visa document approvals.</summary>
    public const string RequestType = "VisaDocument";

    public async Task<Result<SubmitVisaDocumentRequestResponse>> Handle(
        SubmitVisaDocumentRequestCommand command,
        CancellationToken cancellationToken)
    {
        var approverEmployeeId = await visaSettings.GetApproverEmployeeIdAsync(tenant.TenantId, cancellationToken);
        if (approverEmployeeId is null || approverEmployeeId == Guid.Empty)
        {
            logger.LogWarning("Visa approver not configured for tenant {TenantId}", tenant.TenantId);
            return Result.Failure<SubmitVisaDocumentRequestResponse>(
                Error.Validation(VisaDomainErrors.VisaDocumentRequestApproverNotConfigured));
        }

        var requestResult = VisaDocumentRequest.Create(
            tenantId: tenant.TenantId,
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

        // Single-step approval chain (v1): one configured approver, required.
        var steps = new[] { new ApprovalStepInput(StepNumber: 1, ApproverEmployeeId: approverEmployeeId.Value, IsRequired: true) };

        var approvalResult = await workflowService.StartAsync(
            requestType: RequestType,
            subjectId: request.Id,
            requesterEmployeeId: command.EmployeeId,
            explicitSteps: steps,
            submittedIpAddress: null,
            submittedUserAgent: null,
            ct: cancellationToken);

        if (approvalResult.IsFailure)
        {
            logger.LogError("Workflow approval could not be started for visa request: {ErrorCode}", approvalResult.Error.Code);
            return Result.Failure<SubmitVisaDocumentRequestResponse>(approvalResult.Error);
        }

        var approvalRequestId = approvalResult.Value;
        request.SubmitForApproval(approvalRequestId);

        await visaRequests.AddAsync(request, cancellationToken);
        await visaRequests.SaveAsync(cancellationToken);

        logger.LogInformation(
            "Visa document request {RequestId} submitted (approval {ApprovalRequestId})",
            request.Id,
            approvalRequestId);

        return Result.Success(new SubmitVisaDocumentRequestResponse(
            RequestId: request.Id,
            Status: request.Status,
            ApprovalRequestId: approvalRequestId));
    }
}

public sealed record SubmitVisaDocumentRequestResponse(
    Guid RequestId,
    VisaDocumentRequestStatus Status,
    Guid ApprovalRequestId);

/// <summary>
/// Approves a visa document request and delivers the configured template documents
/// into the employee's folder. Invoked by the Workflow approval-completed handler.
/// </summary>
[RequiresPermission("documents.visa.approve")]
public sealed record ApproveVisaDocumentRequestCommand(Guid RequestId) : ICommand<ApproveVisaDocumentRequestResponse>;

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
    IVisaDocumentDeliveryService documentDelivery,
    ILogger<ApproveVisaDocumentRequestHandler> logger)
    : ICommandHandler<ApproveVisaDocumentRequestCommand, ApproveVisaDocumentRequestResponse>
{
    public async Task<Result<ApproveVisaDocumentRequestResponse>> Handle(
        ApproveVisaDocumentRequestCommand command,
        CancellationToken cancellationToken)
    {
        var request = await visaRequests.GetByIdAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return Result.Failure<ApproveVisaDocumentRequestResponse>(
                Error.NotFound(VisaDomainErrors.VisaDocumentRequestNotFound));
        }

        if (request.TenantId != tenant.TenantId)
        {
            logger.LogWarning("Cross-tenant visa request access blocked for request {RequestId}", command.RequestId);
            return Result.Failure<ApproveVisaDocumentRequestResponse>(
                Error.Forbidden(DocumentsDomainErrors.TenantNotAuthorized));
        }

        request.Approve();

        var deliveryResult = await documentDelivery.DeliverVisaDocumentsAsync(
            request.TenantId,
            request.EmployeeId,
            request.Id,
            cancellationToken);

        if (deliveryResult.IsFailure)
        {
            logger.LogError("Visa document delivery failed for request {RequestId}: {ErrorCode}", request.Id, deliveryResult.Error.Code);
            return Result.Failure<ApproveVisaDocumentRequestResponse>(deliveryResult.Error);
        }

        request.Complete();
        await visaRequests.SaveAsync(cancellationToken);

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

/// <summary>Rejects a visa document request, recording the reason shown to the employee.</summary>
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
        RuleFor(x => x.RejectionReason).NotEmpty().MaximumLength(VisaLimits.RejectionReasonMaxLength);
    }
}

public sealed class RejectVisaDocumentRequestHandler(
    ITenantContext tenant,
    IVisaDocumentRequestRepository visaRequests,
    ILogger<RejectVisaDocumentRequestHandler> logger)
    : ICommandHandler<RejectVisaDocumentRequestCommand, RejectVisaDocumentRequestResponse>
{
    public async Task<Result<RejectVisaDocumentRequestResponse>> Handle(
        RejectVisaDocumentRequestCommand command,
        CancellationToken cancellationToken)
    {
        var request = await visaRequests.GetByIdAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return Result.Failure<RejectVisaDocumentRequestResponse>(
                Error.NotFound(VisaDomainErrors.VisaDocumentRequestNotFound));
        }

        if (request.TenantId != tenant.TenantId)
        {
            return Result.Failure<RejectVisaDocumentRequestResponse>(
                Error.Forbidden(DocumentsDomainErrors.TenantNotAuthorized));
        }

        request.Reject(command.RejectionReason, command.RejectedBy);
        await visaRequests.SaveAsync(cancellationToken);

        logger.LogInformation("Visa document request {RequestId} rejected", request.Id);

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
