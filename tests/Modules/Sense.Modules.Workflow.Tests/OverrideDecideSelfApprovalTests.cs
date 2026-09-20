using NSubstitute;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>Regresyon: Admin override yolu atanan kontrolünü atlar ama karar veren talep sahibiyse reddedilmelidir (self-approval).</summary>
public sealed class OverrideDecideSelfApprovalTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _requester = Guid.NewGuid();
    private readonly Guid _approver = Guid.NewGuid();

    private (OverrideDecideApprovalTaskHandler Handler, ApprovalRequest Request) Arrange()
    {
        var request = ApprovalRequest.Submit(
            _tenantId, "LeaveRequest", Guid.NewGuid(), _requester, [new ApprovalTaskSeed(1, _approver, null, true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;
        var repo = Substitute.For<IApprovalRequestRepository>();
        repo.GetByTaskIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Sense.Shared.Kernel.Results.Result.Success(request));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        return (new OverrideDecideApprovalTaskHandler(tenant, repo, Substitute.For<IIntegrationEventOutbox>(), TimeProvider.System), request);
    }

    private static OverrideDecideApprovalTaskCommand Cmd(Guid taskId, Guid decider) =>
        new(taskId, decider, ApprovalOutcome.Approved, null, ApprovalChannel.Web, null, null);

    [Fact]
    public async Task Override_ByRequester_IsRejected_AndRequestStaysPending()
    {
        var (handler, request) = Arrange();

        var result = await handler.Handle(Cmd(request.Tasks.Single().Id, _requester), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowErrors.SelfApprovalNotAllowed);
        request.Status.ShouldBe(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task Override_ByAnotherEmployee_StillWorks()
    {
        var (handler, request) = Arrange();

        var result = await handler.Handle(Cmd(request.Tasks.Single().Id, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalRequestStatus.Approved);
    }
}
