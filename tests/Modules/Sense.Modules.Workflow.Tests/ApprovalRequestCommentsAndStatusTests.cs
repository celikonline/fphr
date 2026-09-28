using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// FG-009 with every port faked: the party-only access matrix shared by the status read and both comment endpoints
/// (workflow.inbox.view alone no longer opens a request - every employee holds it), the step timeline projection and
/// the comment notification's recipient list. The request: step 1 assigned to <see cref="_approver"/> but decided by
/// <see cref="_decider"/> (override), step 2 pending on the HR role queue.
/// </summary>
public sealed class ApprovalRequestCommentsAndStatusTests
{
    private const string OverridePermission = "workflow.request.override";
    private const string InboxViewPermission = "workflow.inbox.view";
    private const string HrRole = "HR";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _requester = Guid.NewGuid();
    private readonly Guid _approver = Guid.NewGuid();
    private readonly Guid _decider = Guid.NewGuid();
    private readonly Guid _hrMember = Guid.NewGuid();
    private readonly Guid _secondHrMember = Guid.NewGuid();
    private readonly Guid _overrideHolder = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IApprovalRequestRepository _requests = Substitute.For<IApprovalRequestRepository>();
    private readonly IRoleAssignmentLookup _roleAssignments = Substitute.For<IRoleAssignmentLookup>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();

    public enum Caller
    {
        Requester,
        AssignedApprover,
        Decider,
        FallbackRoleMember,
        OverrideHolder,
        OutsiderWithInboxView,
    }

    public ApprovalRequestCommentsAndStatusTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _roleAssignments.EmployeeHasRoleAsync(_tenantId, Arg.Any<Guid>(), HrRole, Arg.Any<CancellationToken>())
            .Returns(ci => (Guid)ci[1] == _hrMember || (Guid)ci[1] == _secondHrMember);
        _roleAssignments.ListEmployeeIdsInRoleAsync(_tenantId, HrRole, Arg.Any<CancellationToken>())
            .Returns([_hrMember, _secondHrMember]);
        _employeeDirectory.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<Guid>)ci[1])
                .Select(id => new EmployeeContact(id, "Ad", id == _decider ? "Karar" : "Kişi", null, null, null, "Active"))
                .ToList());
    }

    private ApprovalRequest SeedRequest(bool showCurrentApproverToRequester = true)
    {
        var request = ApprovalRequest.Submit(
            _tenantId, "Leave", Guid.NewGuid(), _requester,
            [new ApprovalTaskSeed(1, _approver, null, true), new ApprovalTaskSeed(2, null, HrRole, true)],
            null, null, null, false, showCurrentApproverToRequester, _now.AddHours(-2), null, null).Value;
        request.Decide(1, _decider, ApprovalOutcome.Approved, "Uygun", wasOverride: true, ApprovalChannel.Web, null, null, _now.AddHours(-1))
            .IsSuccess.ShouldBeTrue();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        return request;
    }

    private Guid ActAs(Caller caller)
    {
        var employeeId = caller switch
        {
            Caller.Requester => _requester,
            Caller.AssignedApprover => _approver,
            Caller.Decider => _decider,
            Caller.FallbackRoleMember => _hrMember,
            Caller.OverrideHolder => _overrideHolder,
            _ => _outsider,
        };
        _currentUser.EmployeeId.Returns(employeeId);
        _currentUser.UserId.Returns(_userId);
        _permissions.HasAsync(_userId, InboxViewPermission, Arg.Any<CancellationToken>()).Returns(true);
        _permissions.HasAsync(_userId, OverridePermission, Arg.Any<CancellationToken>()).Returns(caller == Caller.OverrideHolder);
        return employeeId;
    }

    private GetApprovalStatusHandler StatusHandler() =>
        new(_currentUser, _tenant, _requests, _roleAssignments, _permissions, _employeeDirectory);

    private ListApprovalRequestCommentsHandler CommentsHandler() =>
        new(_currentUser, _tenant, _requests, _roleAssignments, _permissions, _employeeDirectory);

    private AddApprovalRequestCommentHandler AddCommentHandler() =>
        new(_currentUser, _tenant, _requests, _roleAssignments, _permissions, _outbox, TimeProvider.System);

    [Theory]
    [InlineData(Caller.Requester, true)]
    [InlineData(Caller.AssignedApprover, true)]
    [InlineData(Caller.Decider, true)]
    [InlineData(Caller.FallbackRoleMember, true)]
    [InlineData(Caller.OverrideHolder, true)]
    [InlineData(Caller.OutsiderWithInboxView, false)]
    public async Task AccessMatrix_StatusAndComments_OnlyPartiesSeeTheRequest_OthersGetNotFound(Caller caller, bool allowed)
    {
        var request = SeedRequest();
        var employeeId = ActAs(caller);

        var status = await StatusHandler().Handle(new GetApprovalStatusQuery(request.Id), CancellationToken.None);
        var comments = await CommentsHandler().Handle(new ListApprovalRequestCommentsQuery(request.Id), CancellationToken.None);
        var added = await AddCommentHandler().Handle(
            new AddApprovalRequestCommentCommand(request.Id, employeeId, "Ek bilgi rica ederim"), CancellationToken.None);

        if (allowed)
        {
            status.IsSuccess.ShouldBeTrue();
            comments.IsSuccess.ShouldBeTrue();
            added.IsSuccess.ShouldBeTrue();
            request.History.ShouldContain(h => h.EventType == ApprovalHistoryEventType.Commented && h.ActorEmployeeId == employeeId);
        }
        else
        {
            foreach (var error in new[] { status.Error, comments.Error, added.Error })
            {
                error.Type.ShouldBe(ErrorType.NotFound);
                error.Code.ShouldBe(WorkflowErrors.RequestNotFound);
            }

            request.History.ShouldNotContain(h => h.EventType == ApprovalHistoryEventType.Commented);
            _outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
        }
    }

    [Fact]
    public async Task Status_ReturnsTheStepTimeline_DecidedStepNamesTheDecider_RoleQueueStepHasNoApprover()
    {
        var request = SeedRequest();
        ActAs(Caller.Requester);

        var result = await StatusHandler().Handle(new GetApprovalStatusQuery(request.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.SubmittedAt.ShouldBe(_now.AddHours(-2));
        dto.CompletedAt.ShouldBeNull();
        dto.CurrentStepNumber.ShouldBe(2);
        dto.Steps.Select(s => s.StepNumber).ShouldBe([1, 2]);

        var decided = dto.Steps[0];
        decided.Status.ShouldBe(ApprovalTaskStatus.Approved);
        decided.ApproverEmployeeId.ShouldBe(_decider);
        decided.ApproverName.ShouldBe("Ad Karar");
        decided.DecidedAt.ShouldBe(_now.AddHours(-1));
        decided.Comment.ShouldBe("Uygun");
        decided.IsFallbackRole.ShouldBeFalse();

        var pending = dto.Steps[1];
        pending.Status.ShouldBe(ApprovalTaskStatus.Pending);
        pending.ApproverEmployeeId.ShouldBeNull();
        pending.ApproverName.ShouldBeNull();
        pending.IsFallbackRole.ShouldBeTrue();
        pending.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Status_PolicyHidesApprover_HiddenFromRequesterOnly()
    {
        var request = ApprovalRequest.Submit(
            _tenantId, "Leave", Guid.NewGuid(), _requester, [new ApprovalTaskSeed(1, _approver, null, true)],
            null, null, null, false, showCurrentApproverToRequester: false, _now, null, null).Value;
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(request));

        ActAs(Caller.Requester);
        var asRequester = await StatusHandler().Handle(new GetApprovalStatusQuery(request.Id), CancellationToken.None);
        ActAs(Caller.AssignedApprover);
        var asApprover = await StatusHandler().Handle(new GetApprovalStatusQuery(request.Id), CancellationToken.None);

        var hidden = asRequester.Value.Steps.ShouldHaveSingleItem();
        hidden.ApproverEmployeeId.ShouldBeNull();
        hidden.ApproverName.ShouldBeNull();
        asApprover.Value.Steps.ShouldHaveSingleItem().ApproverEmployeeId.ShouldBe(_approver);
    }

    [Fact]
    public async Task Comments_AreListedOldestFirst_WithAuthorNames()
    {
        var request = SeedRequest();
        request.AddComment(_requester, "  İlk yorum  ", _now).IsSuccess.ShouldBeTrue();
        request.AddComment(_hrMember, "İkinci yorum", _now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        ActAs(Caller.AssignedApprover);

        var result = await CommentsHandler().Handle(new ListApprovalRequestCommentsQuery(request.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(c => c.Text).ShouldBe(["İlk yorum", "İkinci yorum"]);
        result.Value.Select(c => c.AuthorEmployeeId).ShouldBe([_requester, _hrMember]);
        result.Value[0].AuthorName.ShouldBe("Ad Kişi");
        result.Value[0].CreatedAt.ShouldBe(_now);
    }

    [Fact]
    public async Task AddComment_ByRequester_NotifiesApproverDeciderAndPendingRoleQueue_ButNotTheAuthor()
    {
        var request = SeedRequest();
        ActAs(Caller.Requester);
        ApprovalRequestCommented? published = null;
        _outbox.When(o => o.Enqueue(Arg.Any<ApprovalRequestCommented>())).Do(ci => published = (ApprovalRequestCommented)ci[0]);

        var result = await AddCommentHandler().Handle(
            new AddApprovalRequestCommentCommand(request.Id, _requester, "Belgeyi ekledim"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(request.History[^1].Id);
        published.ShouldNotBeNull();
        published.ApprovalRequestId.ShouldBe(request.Id);
        published.AuthorEmployeeId.ShouldBe(_requester);
        published.RecipientEmployeeIds.ShouldBe([_approver, _decider, _hrMember, _secondHrMember], ignoreOrder: true);
    }

    [Fact]
    public async Task AddComment_ByRoleQueueMember_NotifiesTheRequester_AndNeverTheAuthor()
    {
        var request = SeedRequest();
        ActAs(Caller.FallbackRoleMember);
        ApprovalRequestCommented? published = null;
        _outbox.When(o => o.Enqueue(Arg.Any<ApprovalRequestCommented>())).Do(ci => published = (ApprovalRequestCommented)ci[0]);

        (await AddCommentHandler().Handle(new AddApprovalRequestCommentCommand(request.Id, _hrMember, "Belge eksik"), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        published.ShouldNotBeNull();
        published.RecipientEmployeeIds.ShouldContain(_requester);
        published.RecipientEmployeeIds.ShouldNotContain(_hrMember);
    }

    [Fact]
    public async Task AddComment_OnCancelledRequest_FailsWithRequestClosed_AndPublishesNothing()
    {
        var request = SeedRequest();
        request.Cancel(_requester, _now).IsSuccess.ShouldBeTrue();
        ActAs(Caller.Requester);

        var result = await AddCommentHandler().Handle(
            new AddApprovalRequestCommentCommand(request.Id, _requester, "Geç kaldım"), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(WorkflowDomainErrors.CommentRequestClosed);
        _outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public void Domain_AddComment_RecordsACommentedHistoryEntry_AllowedAfterCompletion_RejectsBlankText()
    {
        var request = SeedRequest();

        request.AddComment(_requester, "   ", _now).Error.Code.ShouldBe(WorkflowDomainErrors.CommentTextRequired);

        var entry = request.AddComment(_hrMember, " Kontrol ediyorum ", _now).Value;
        entry.EventType.ShouldBe(ApprovalHistoryEventType.Commented);
        entry.ActorEmployeeId.ShouldBe(_hrMember);
        entry.Note.ShouldBe("Kontrol ediyorum");
        entry.StepNumber.ShouldBe(2);
        entry.OccurredAt.ShouldBe(_now);

        request.Decide(2, _hrMember, ApprovalOutcome.Approved, null, false, ApprovalChannel.Web, null, null, _now).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalRequestStatus.Approved);
        var afterCompletion = request.AddComment(_requester, "Teşekkürler", _now.AddMinutes(1));
        afterCompletion.IsSuccess.ShouldBeTrue();
        afterCompletion.Value.StepNumber.ShouldBeNull();
    }
}
