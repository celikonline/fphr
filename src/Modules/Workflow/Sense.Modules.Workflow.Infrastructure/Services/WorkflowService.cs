using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Infrastructure.Services;

/// <summary>
/// Implements Workflow.Contracts.IWorkflowService - the only way other modules start/cancel an approval process.
/// Delegates to the dispatcher so the command pipeline (UnitOfWork, outbox, validation) applies exactly as it does
/// for Workflow's own controllers.
/// </summary>
public sealed class WorkflowService(IDispatcher dispatcher) : IWorkflowService
{
    public Task<Result<Guid>> StartAsync(
        string requestType, Guid subjectId, Guid requesterEmployeeId, IReadOnlyList<ApprovalStepInput>? explicitSteps,
        string? submittedIpAddress, string? submittedUserAgent, CancellationToken ct = default) =>
        dispatcher.Send(
            new SubmitApprovalRequestCommand(requestType, subjectId, requesterEmployeeId, explicitSteps, submittedIpAddress, submittedUserAgent),
            ct);

    public Task<Result> CancelAsync(Guid approvalRequestId, Guid actorEmployeeId, CancellationToken ct = default) =>
        dispatcher.Send(new CancelApprovalRequestCommand(approvalRequestId, actorEmployeeId), ct);
}
