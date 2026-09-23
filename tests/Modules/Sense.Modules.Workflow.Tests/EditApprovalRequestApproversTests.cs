using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// TK-009 "talep anında onaycı listesi düzenleme": a request's own approver assignment can be reassigned once,
/// only while pending, only for still-pending (undecided) tasks, and only by the requester or their direct manager.
/// </summary>
public sealed class EditApprovalRequestApproversTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _requester = Guid.NewGuid();
    private readonly Guid _manager = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();
    private readonly Guid _originalApprover1 = Guid.NewGuid();
    private readonly Guid _originalApprover2 = Guid.NewGuid();
    private readonly Guid _newApprover = Guid.NewGuid();

    private ApprovalRequest TwoStepRequest() =>
        ApprovalRequest.Submit(
            _tenantId, "LeaveRequest", Guid.NewGuid(), _requester,
            [new ApprovalTaskSeed(1, _originalApprover1, null, true), new ApprovalTaskSeed(2, _originalApprover2, null, true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;

    // --- Domain: ApprovalRequest.EditApprovers ---

    [Fact]
    public void EditApprovers_OnPendingFutureStep_ReassignsApprover()
    {
        var request = TwoStepRequest();

        var result = request.EditApprovers([(2, _newApprover)], _requester, DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        request.Tasks.Single(t => t.StepNumber == 2).ApproverEmployeeId.ShouldBe(_newApprover);
        request.Tasks.Single(t => t.StepNumber == 1).ApproverEmployeeId.ShouldBe(_originalApprover1);
    }

    [Fact]
    public void EditApprovers_SecondAttempt_Fails_AlreadyEdited()
    {
        var request = TwoStepRequest();
        request.EditApprovers([(2, _newApprover)], _requester, DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var second = request.EditApprovers([(2, Guid.NewGuid())], _requester, DateTime.UtcNow);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(WorkflowDomainErrors.RequestApproversAlreadyEdited);
    }

    [Fact]
    public void EditApprovers_OnAlreadyDecidedStep_Fails_TaskNotPending()
    {
        var request = TwoStepRequest();
        request.Decide(1, _originalApprover1, ApprovalOutcome.Approved, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow)
            .IsSuccess.ShouldBeTrue();

        var result = request.EditApprovers([(1, _newApprover)], _requester, DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.TaskNotPending);
    }

    [Fact]
    public void EditApprovers_AfterRequestCompleted_Fails_RequestNotPending()
    {
        var request = ApprovalRequest.Submit(
            _tenantId, "LeaveRequest", Guid.NewGuid(), _requester, [new ApprovalTaskSeed(1, _originalApprover1, null, true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;
        request.Decide(1, _originalApprover1, ApprovalOutcome.Approved, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow)
            .IsSuccess.ShouldBeTrue();

        var result = request.EditApprovers([(1, _newApprover)], _requester, DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.RequestNotPending);
    }

    // --- Application: EditApprovalRequestApproversHandler ---

    private EditApprovalRequestApproversHandler Handler(ApprovalRequest request)
    {
        var repo = Substitute.For<IApprovalRequestRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(request));

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);

        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.GetManagerEmployeeIdAsync(_tenantId, _requester, Arg.Any<CancellationToken>()).Returns(_manager);
        employeeLookup.ExistsActiveAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        return new EditApprovalRequestApproversHandler(tenant, repo, employeeLookup, Substitute.For<IIntegrationEventOutbox>(), TimeProvider.System);
    }

    [Fact]
    public async Task Handle_ByRequester_Succeeds()
    {
        var request = TwoStepRequest();
        var handler = Handler(request);

        var result = await handler.Handle(
            new EditApprovalRequestApproversCommand(request.Id, _requester, [new ApprovalStepApproverEdit(2, _newApprover)]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        request.Tasks.Single(t => t.StepNumber == 2).ApproverEmployeeId.ShouldBe(_newApprover);
    }

    [Fact]
    public async Task Handle_ByRequesterManager_Succeeds()
    {
        var request = TwoStepRequest();
        var handler = Handler(request);

        var result = await handler.Handle(
            new EditApprovalRequestApproversCommand(request.Id, _manager, [new ApprovalStepApproverEdit(2, _newApprover)]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_ByUnrelatedEmployee_Fails_NotAuthorized()
    {
        var request = TwoStepRequest();
        var handler = Handler(request);

        var result = await handler.Handle(
            new EditApprovalRequestApproversCommand(request.Id, _stranger, [new ApprovalStepApproverEdit(2, _newApprover)]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowErrors.EditApproversNotAuthorized);
        request.Tasks.Single(t => t.StepNumber == 2).ApproverEmployeeId.ShouldBe(_originalApprover2);
    }

    [Fact]
    public async Task Handle_WithInactiveNewApprover_Fails_PolicyStepApproverInvalid()
    {
        var request = TwoStepRequest();
        var repo = Substitute.For<IApprovalRequestRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.GetManagerEmployeeIdAsync(_tenantId, _requester, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        employeeLookup.ExistsActiveAsync(_tenantId, _newApprover, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new EditApprovalRequestApproversHandler(tenant, repo, employeeLookup, Substitute.For<IIntegrationEventOutbox>(), TimeProvider.System);

        var result = await handler.Handle(
            new EditApprovalRequestApproversCommand(request.Id, _requester, [new ApprovalStepApproverEdit(2, _newApprover)]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowErrors.PolicyStepApproverInvalid);
    }
}
