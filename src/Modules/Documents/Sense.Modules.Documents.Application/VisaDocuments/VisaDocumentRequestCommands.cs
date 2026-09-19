using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>
/// Submits the caller's own visa document request and starts the Workflow approval process for it. The requester is
/// always the signed-in employee (never a client-supplied id). The approver is read from the tenant's visa settings; a
/// missing approver is a hard failure (the request is not created) so the employee gets an actionable error instead of
/// a silent no-op.
/// </summary>
[RequiresPermission("documents.my.visa_requests")]
public sealed record SubmitVisaDocumentRequestCommand(
    Guid? VisaDocumentProcessId,
    DateTime TravelStart,
    DateTime TravelEnd,
    string CountryCode,
    string VisaType,
    string? Description) : ICommand<SubmitVisaDocumentRequestResponse>;

public sealed class SubmitVisaDocumentRequestValidator : AbstractValidator<SubmitVisaDocumentRequestCommand>
{
    public SubmitVisaDocumentRequestValidator()
    {
        RuleFor(x => x.TravelStart).NotEmpty();
        RuleFor(x => x.TravelEnd).NotEmpty();
        RuleFor(x => x.CountryCode).NotEmpty().Length(VisaLimits.CountryCodeMinLength, VisaLimits.CountryCodeMaxLength)
            .Must(code => code.All(char.IsAsciiLetter))
            .WithErrorCode(VisaDomainErrors.VisaDocumentRequestInvalidCountryCode);
        RuleFor(x => x.VisaType).NotEmpty().Must(VisaTypes.IsKnown)
            .WithErrorCode(VisaDomainErrors.VisaDocumentRequestInvalidVisaType);
        RuleFor(x => x.Description).MaximumLength(VisaLimits.DescriptionMaxLength);
    }
}

public sealed class SubmitVisaDocumentRequestHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IVisaDocumentRequestRepository visaRequests,
    IVisaDocumentProcessRepository processes,
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
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(VisaDomainErrors.VisaDocumentRequestNoEmployeeForUser);
        }

        var approverEmployeeId = await visaSettings.GetApproverEmployeeIdAsync(tenant.TenantId, cancellationToken);
        if (approverEmployeeId is null || approverEmployeeId == Guid.Empty)
        {
            logger.LogWarning("Visa approver not configured for tenant {TenantId}", tenant.TenantId);
            return Error.Validation(VisaDomainErrors.VisaDocumentRequestApproverNotConfigured);
        }

        if (command.VisaDocumentProcessId is { } processId
            && await processes.GetByIdAsync(processId, cancellationToken) is not { IsActive: true })
        {
            return Error.Validation(VisaDomainErrors.VisaDocumentRequestProcessUnavailable);
        }

        var requestResult = VisaDocumentRequest.Create(
            tenantId: tenant.TenantId,
            employeeId: employeeId,
            visaDocumentProcessId: command.VisaDocumentProcessId,
            travelStart: AsUtcDate(command.TravelStart),
            travelEnd: AsUtcDate(command.TravelEnd),
            countryCode: command.CountryCode.ToUpperInvariant(),
            visaType: command.VisaType,
            description: command.Description?.Trim() ?? string.Empty);

        if (requestResult.IsFailure)
        {
            return requestResult.Error;
        }

        var request = requestResult.Value;

        // Single-step approval chain (v1): the tenant's one configured approver, required.
        var steps = new[] { new ApprovalStepInput(StepNumber: 1, ApproverEmployeeId: approverEmployeeId.Value, IsRequired: true) };

        var approvalResult = await workflowService.StartAsync(
            requestType: RequestType,
            subjectId: request.Id,
            requesterEmployeeId: employeeId,
            explicitSteps: steps,
            submittedIpAddress: currentUser.IpAddress,
            submittedUserAgent: null,
            ct: cancellationToken);

        if (approvalResult.IsFailure)
        {
            logger.LogError("Workflow approval could not be started for visa request: {ErrorCode}", approvalResult.Error.Code);
            return approvalResult.Error;
        }

        request.SubmitForApproval(approvalResult.Value);

        try
        {
            await visaRequests.AddAsync(request, cancellationToken);
            await visaRequests.SaveAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The approval was already committed in Workflow's own unit of work; without the request row it would sit in the
            // approver's inbox for a subject that does not exist. Best-effort compensation, then surface the original failure.
            logger.LogError(ex, "Saving visa request {RequestId} failed after approval {ApprovalRequestId} started; cancelling it", request.Id, approvalResult.Value);
            await workflowService.CancelAsync(approvalResult.Value, employeeId, CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "Visa document request {RequestId} submitted (approval {ApprovalRequestId})",
            request.Id,
            approvalResult.Value);

        return new SubmitVisaDocumentRequestResponse(request.Id, request.Status, approvalResult.Value);
    }

    /// <summary>Travel dates are calendar days: the clock part is dropped and the value marked UTC (Npgsql refuses
    /// Unspecified-kind DateTime for timestamptz columns, which is what a plain "2026-11-01" body value binds to).</summary>
    private static DateTime AsUtcDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}

public sealed record SubmitVisaDocumentRequestResponse(
    Guid RequestId,
    VisaDocumentRequestStatus Status,
    Guid ApprovalRequestId);
