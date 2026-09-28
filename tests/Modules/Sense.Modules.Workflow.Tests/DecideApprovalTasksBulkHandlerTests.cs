using NSubstitute;
using Sense.Modules.Workflow.Api.Controllers;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// FG-008 decide-bulk: call-level checks (empty, over the limit, rejection without comment) fail the whole call before
/// any task is touched; otherwise each distinct task goes to the isolated runner as its own DecideApprovalTaskCommand
/// and the answer is per task. The runner (child scope + own transaction) is covered by ApprovalBulkDecisionIntegrationTests.
/// </summary>
public sealed class DecideApprovalTasksBulkHandlerTests
{
    private readonly IIsolatedApprovalDecisionRunner _runner = Substitute.For<IIsolatedApprovalDecisionRunner>();
    private readonly Guid _approver = Guid.NewGuid();

    private DecideApprovalTasksBulkHandler Handler => new(_runner);

    private DecideApprovalTasksBulkCommand Command(IReadOnlyList<Guid>? taskIds, ApprovalOutcome outcome = ApprovalOutcome.Approved, string? comment = null) =>
        new(taskIds, _approver, outcome, comment, ApprovalChannel.Mobile, "203.0.113.9", "SenseMobile/1.0");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_NoTaskIds_FailsWithBulkEmpty(bool nullList)
    {
        var result = await Handler.Handle(Command(nullList ? null : []), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(WorkflowErrors.BulkEmpty);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        await _runner.DidNotReceiveWithAnyArgs().DecideAsync(default!, default);
    }

    [Fact]
    public async Task Handle_MoreThanTheLimit_FailsWithBulkTooManyTasks()
    {
        var taskIds = Enumerable.Range(0, WorkflowLimits.BulkDecisionMaxTasks + 1).Select(_ => Guid.NewGuid()).ToList();

        var result = await Handler.Handle(Command(taskIds), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(WorkflowErrors.BulkTooManyTasks);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Args!["max"].ShouldBe(WorkflowLimits.BulkDecisionMaxTasks);
        await _runner.DidNotReceiveWithAnyArgs().DecideAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ExactlyTheLimit_DecidesEveryTask()
    {
        var taskIds = Enumerable.Range(0, WorkflowLimits.BulkDecisionMaxTasks).Select(_ => Guid.NewGuid()).ToList();
        _runner.DecideAsync(Arg.Any<DecideApprovalTaskCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        var result = await Handler.Handle(Command(taskIds), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(WorkflowLimits.BulkDecisionMaxTasks);
        result.Value.ShouldAllBe(r => r.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Handle_RejectWithoutComment_FailsTheWholeCall(string? comment)
    {
        var result = await Handler.Handle(Command([Guid.NewGuid(), Guid.NewGuid()], ApprovalOutcome.Rejected, comment), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(WorkflowDomainErrors.RejectionCommentRequired);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        await _runner.DidNotReceiveWithAnyArgs().DecideAsync(default!, default);
    }

    [Fact]
    public async Task Handle_OneTaskFails_OthersStillDecided_ResultPerTaskInRequestOrder()
    {
        var mine1 = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var mine2 = Guid.NewGuid();
        _runner.DecideAsync(Arg.Any<DecideApprovalTaskCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _runner.DecideAsync(Arg.Is<DecideApprovalTaskCommand>(c => c.TaskId == foreign), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Rule(WorkflowErrors.AssigneeMismatch)));

        var result = await Handler.Handle(Command([mine1, foreign, mine2]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(r => r.TaskId).ShouldBe([mine1, foreign, mine2]);
        result.Value.Select(r => r.Succeeded).ShouldBe([true, false, true]);
        result.Value[1].Error!.Code.ShouldBe(WorkflowErrors.AssigneeMismatch);
        await _runner.Received(3).DecideAsync(Arg.Any<DecideApprovalTaskCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PassesCallerOutcomeCommentAndChannelToEveryTaskCommand()
    {
        var taskId = Guid.NewGuid();
        _runner.DecideAsync(Arg.Any<DecideApprovalTaskCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await Handler.Handle(Command([taskId], ApprovalOutcome.Rejected, "Bütçe uygun değil"), TestContext.Current.CancellationToken);

        await _runner.Received(1).DecideAsync(
            new DecideApprovalTaskCommand(taskId, _approver, ApprovalOutcome.Rejected, "Bütçe uygun değil", ApprovalChannel.Mobile, "203.0.113.9", "SenseMobile/1.0"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateTaskIds_DecidedOnce()
    {
        var taskId = Guid.NewGuid();
        var other = Guid.NewGuid();
        _runner.DecideAsync(Arg.Any<DecideApprovalTaskCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        var result = await Handler.Handle(Command([taskId, other, taskId]), TestContext.Current.CancellationToken);

        result.Value.Select(r => r.TaskId).ShouldBe([taskId, other]);
        await _runner.Received(1).DecideAsync(Arg.Is<DecideApprovalTaskCommand>(c => c.TaskId == taskId), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("mobile", ApprovalChannel.Mobile)]
    [InlineData("Mobile", ApprovalChannel.Mobile)]
    [InlineData(" mobile ", ApprovalChannel.Mobile)]
    [InlineData("web", ApprovalChannel.Web)]
    [InlineData("", ApprovalChannel.Web)]
    [InlineData(null, ApprovalChannel.Web)]
    [InlineData("integration", ApprovalChannel.Web)]
    public void ClientChannel_OnlyMobileClientAppMapsToMobile(string? clientApp, ApprovalChannel expected) =>
        ApprovalClientChannel.From(clientApp).ShouldBe(expected);

    [Fact]
    public void Mobile_IsAppendedAfterTheExistingChannels()
    {
        // Stored as a string, but the numeric order is still part of the contract - existing values must not shift.
        ((int)ApprovalChannel.Web).ShouldBe(0);
        ((int)ApprovalChannel.Email).ShouldBe(1);
        ((int)ApprovalChannel.Api).ShouldBe(2);
        ((int)ApprovalChannel.Mobile).ShouldBe(3);
    }
}
