using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Application.Delegations;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Domain.Delegations;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests.Delegations;

/// <summary>Z2.2 vekalet: süre, iptal, kapsam, zincirsizlik ve "adına karar" kaydı (Plan Review Focus 5).</summary>
public sealed class DelegationTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 10, 10);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _delegator = Guid.NewGuid();
    private readonly Guid _delegate = Guid.NewGuid();
    private readonly Guid _requester = Guid.NewGuid();
    private readonly InMemoryDelegations _store = new();
    private readonly MutableClock _clock = new(new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc));

    private ApprovalDelegation NewDelegation(Guid? delegator = null, Guid? delegateId = null, IReadOnlyCollection<string>? types = null)
    {
        var d = ApprovalDelegation.Create(_tenantId, delegator ?? _delegator, delegateId ?? _delegate, Start, End, types).Value;
        _store.Items.Add(d);
        return d;
    }

    private ApprovalRequest NewRequest(Guid assignee, string requestType = "Leave") =>
        ApprovalRequest.Submit(
            _tenantId, requestType, Guid.NewGuid(), _requester, [new ApprovalTaskSeed(1, assignee, null, true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;

    private DelegationResolver Resolver => new(_store);

    private (DecideApprovalTaskHandler Handler, IIntegrationEventOutbox Outbox) DecideHandler(ApprovalRequest request)
    {
        var repo = Substitute.For<IApprovalRequestRepository>();
        repo.GetByTaskIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        return (new DecideApprovalTaskHandler(tenant, repo, Substitute.For<IRoleAssignmentLookup>(), outbox, _clock, Resolver), outbox);
    }

    private Task<Result> Decide(ApprovalRequest request, Guid decider) =>
        DecideHandler(request).Handler.Handle(
            new DecideApprovalTaskCommand(request.Tasks.Single().Id, decider, ApprovalOutcome.Approved, null, ApprovalChannel.Web, null, null),
            CancellationToken.None);

    [Fact]
    public async Task Delegate_sees_delegator_inbox_within_range()
    {
        NewDelegation();
        var request = NewRequest(_delegator);
        var task = request.Tasks.Single();

        var requests = Substitute.For<IApprovalRequestRepository>();
        requests.ListInboxTasksAsync(_delegate, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<IReadOnlyCollection<Guid>>(2).Contains(_delegator) ? [task] : []);
        requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(_delegate);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        var handler = new ListInboxHandler(currentUser, tenant, requests, Substitute.For<IRoleAssignmentLookup>(), directory, [], Resolver, _clock);

        var inRange = await handler.Handle(new ListInboxQuery(), CancellationToken.None);
        inRange.Value.ShouldHaveSingleItem().TaskId.ShouldBe(task.Id);

        _clock.Now = new DateTime(2026, 10, 11, 0, 0, 1, DateTimeKind.Utc);
        var afterEnd = await handler.Handle(new ListInboxQuery(), CancellationToken.None);
        afterEnd.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delegation_expires_at_end_of_EndDate()
    {
        NewDelegation();

        _clock.Now = new DateTime(2026, 10, 10, 23, 59, 59, DateTimeKind.Utc);
        (await Resolver.ActiveDelegatorsFor(_tenantId, _delegate, DelegationResolver.Today(_clock), "Leave")).ShouldContain(_delegator);

        _clock.Now = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        (await Resolver.ActiveDelegatorsFor(_tenantId, _delegate, DelegationResolver.Today(_clock), "Leave")).ShouldBeEmpty();

        _clock.Now = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc);
        (await Resolver.ActiveDelegatorsFor(_tenantId, _delegate, DelegationResolver.Today(_clock), "Leave")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoked_delegation_blocks_decision()
    {
        var delegation = NewDelegation();
        delegation.Revoke(DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        var request = NewRequest(_delegator);

        var result = await Decide(request, _delegate);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowErrors.AssigneeMismatch);
        request.Status.ShouldBe(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task Delegate_decision_recorded_on_behalf_of()
    {
        NewDelegation();
        var request = NewRequest(_delegator);

        var result = await Decide(request, _delegate);

        result.IsSuccess.ShouldBeTrue();
        var task = request.Tasks.Single();
        task.DecidedByEmployeeId.ShouldBe(_delegate);
        task.DecidedOnBehalfOfEmployeeId.ShouldBe(_delegator);
        request.Status.ShouldBe(ApprovalRequestStatus.Approved);
    }

    [Fact]
    public void Cannot_delegate_to_self()
    {
        var result = ApprovalDelegation.Create(_tenantId, _delegator, _delegator, Start, End, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.DelegationToSelfNotAllowed);
    }

    [Fact]
    public async Task Delegate_cannot_be_delegated_from_delegate_chain()
    {
        // A -> B and B -> C: C must not see or decide A's tasks.
        var a = _delegator;
        var b = _delegate;
        var c = Guid.NewGuid();
        NewDelegation(a, b);
        NewDelegation(b, c);

        (await Resolver.ActiveDelegatorsFor(_tenantId, c, DelegationResolver.Today(_clock), "Leave")).ShouldBe([b]);

        var request = NewRequest(a);
        var result = await Decide(request, c);
        result.Error.Code.ShouldBe(WorkflowErrors.AssigneeMismatch);
    }

    [Fact]
    public async Task RequestTypes_filter_limits_scope()
    {
        NewDelegation(types: ["Expense"]);

        (await Resolver.ActiveDelegatorsFor(_tenantId, _delegate, DelegationResolver.Today(_clock), "Expense")).ShouldContain(_delegator);
        (await Resolver.ActiveDelegatorsFor(_tenantId, _delegate, DelegationResolver.Today(_clock), "Leave")).ShouldBeEmpty();

        var leaveRequest = NewRequest(_delegator, "Leave");
        (await Decide(leaveRequest, _delegate)).Error.Code.ShouldBe(WorkflowErrors.AssigneeMismatch);
        var expenseRequest = NewRequest(_delegator, "Expense");
        (await Decide(expenseRequest, _delegate)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Past_decisions_unchanged_after_revoke()
    {
        var delegation = NewDelegation();
        var request = NewRequest(_delegator);
        (await Decide(request, _delegate)).IsSuccess.ShouldBeTrue();

        delegation.Revoke(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var task = request.Tasks.Single();
        task.Status.ShouldBe(ApprovalTaskStatus.Approved);
        task.DecidedByEmployeeId.ShouldBe(_delegate);
        task.DecidedOnBehalfOfEmployeeId.ShouldBe(_delegator);
        request.Status.ShouldBe(ApprovalRequestStatus.Approved);
    }

    [Fact]
    public async Task Delegate_cannot_decide_own_request()
    {
        NewDelegation();
        var request = ApprovalRequest.Submit(
            _tenantId, "Leave", Guid.NewGuid(), _delegate, [new ApprovalTaskSeed(1, _delegator, null, true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;

        (await Decide(request, _delegate)).Error.Code.ShouldBe(WorkflowErrors.SelfApprovalNotAllowed);
    }

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }

    private sealed class InMemoryDelegations : IApprovalDelegationRepository
    {
        public List<ApprovalDelegation> Items { get; } = [];

        public void Add(ApprovalDelegation delegation) => Items.Add(delegation);

        public Task<Result<ApprovalDelegation>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(d => d.Id == id) is { } d
                ? Result.Success(d)
                : Result.Failure<ApprovalDelegation>(Error.NotFound(WorkflowDomainErrors.DelegationNotFound)));

        public Task<IReadOnlyList<ApprovalDelegation>> ListActiveForDelegateAsync(Guid delegateEmployeeId, DateOnly today, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalDelegation>>(Items
                .Where(d => d.DelegateEmployeeId == delegateEmployeeId && !d.IsRevoked && d.StartDate <= today && d.EndDate >= today).ToList());

        public Task<IReadOnlyList<ApprovalDelegation>> ListInvolvingAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalDelegation>>(Items.Where(d => d.DelegatorEmployeeId == employeeId || d.DelegateEmployeeId == employeeId).ToList());

        public Task<IReadOnlyList<ApprovalDelegation>> ListAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalDelegation>>(Items.ToList());
    }
}
