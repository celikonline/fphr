using NSubstitute;
using Sense.Modules.Identity.Contracts;
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
/// Ürün kararı (denetim A10): bir görev Rejected kararla karara bağlanırken yorum zorunlu; onayda isteğe bağlı.
/// Kural domain'de (<see cref="ApprovalRequest.Decide"/>) durur, böylece web, override ve e-posta linki kanalları aynı kuralı paylaşır.
/// </summary>
public sealed class RejectionCommentRequiredTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _approver = Guid.NewGuid();

    private ApprovalRequest NewRequest(bool required = true) =>
        ApprovalRequest.Submit(
            _tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), [new ApprovalTaskSeed(1, _approver, null, required)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decide_Rejected_WithoutComment_Fails_AndLeavesRequestPending(string? comment)
    {
        var request = NewRequest();

        var result = request.Decide(1, _approver, ApprovalOutcome.Rejected, comment, false, ApprovalChannel.Web, null, null, DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.RejectionCommentRequired);
        request.Status.ShouldBe(ApprovalRequestStatus.Pending);
        request.Tasks.Single().Status.ShouldBe(ApprovalTaskStatus.Pending);
    }

    [Fact]
    public void Decide_Rejected_WithoutComment_OnOptionalStep_AlsoFails()
    {
        var request = NewRequest(required: false);

        var result = request.Decide(1, _approver, ApprovalOutcome.Rejected, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow);

        result.Error.Code.ShouldBe(WorkflowDomainErrors.RejectionCommentRequired);
    }

    [Fact]
    public void Decide_Rejected_WithComment_Succeeds()
    {
        var request = NewRequest();

        var result = request.Decide(1, _approver, ApprovalOutcome.Rejected, "Bütçe uygun değil", false, ApprovalChannel.Web, null, null, DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalRequestStatus.Rejected);
    }

    [Fact]
    public void Decide_Approved_WithoutComment_StillSucceeds()
    {
        var request = NewRequest();

        request.Decide(1, _approver, ApprovalOutcome.Approved, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalRequestStatus.Approved);
    }

    [Fact]
    public async Task DecideHandler_RejectWithoutComment_ReturnsErrorAndKeepsRequestPending()
    {
        var request = NewRequest();
        var repo = Substitute.For<IApprovalRequestRepository>();
        repo.GetByTaskIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var handler = new DecideApprovalTaskHandler(tenant, repo, Substitute.For<IRoleAssignmentLookup>(), Substitute.For<IIntegrationEventOutbox>(), TimeProvider.System);

        var result = await handler.Handle(
            new DecideApprovalTaskCommand(request.Tasks.Single().Id, _approver, ApprovalOutcome.Rejected, "  ", ApprovalChannel.Web, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(WorkflowDomainErrors.RejectionCommentRequired);
        request.Status.ShouldBe(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task OverrideHandler_RejectWithoutComment_ReturnsError()
    {
        var request = NewRequest();
        var repo = Substitute.For<IApprovalRequestRepository>();
        repo.GetByTaskIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var handler = new OverrideDecideApprovalTaskHandler(tenant, repo, Substitute.For<IIntegrationEventOutbox>(), TimeProvider.System);

        var result = await handler.Handle(
            new OverrideDecideApprovalTaskCommand(request.Tasks.Single().Id, Guid.NewGuid(), ApprovalOutcome.Rejected, null, ApprovalChannel.Web, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(WorkflowDomainErrors.RejectionCommentRequired);
        request.Status.ShouldBe(ApprovalRequestStatus.Pending);
    }

    /// <summary>E-posta linki tek kullanımlık: yorumsuz ret, token tüketilmeden reddedilmeli (kullanıcı yorumla yeniden deneyebilsin).</summary>
    [Fact]
    public async Task EmailTokenHandler_RejectWithoutComment_DoesNotConsumeTheToken()
    {
        var request = NewRequest();
        var token = new EmailDecisionToken(request.Tasks.Single().Id, _tenantId, "hash", DateTime.UtcNow.AddDays(1));
        var tokens = Substitute.For<IEmailDecisionTokenRepository>();
        tokens.GetByTokenHashAsync("hash", Arg.Any<CancellationToken>()).Returns(Result.Success(token));
        var requests = Substitute.For<IApprovalRequestRepository>();
        requests.GetByTaskIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var generator = Substitute.For<IEmailTokenGenerator>();
        generator.Hash("raw").Returns("hash");
        var handler = new DecideByEmailTokenHandler(tokens, requests, generator, Substitute.For<IIntegrationEventOutbox>(), TimeProvider.System);

        var result = await handler.Handle(new DecideByEmailTokenCommand("raw", ApprovalOutcome.Rejected, null, null, null), CancellationToken.None);

        result.Error.Code.ShouldBe(WorkflowDomainErrors.RejectionCommentRequired);
        token.UsedAt.ShouldBeNull();
        request.Status.ShouldBe(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task EmailTokenHandler_RejectWithComment_Succeeds()
    {
        var request = NewRequest();
        var token = new EmailDecisionToken(request.Tasks.Single().Id, _tenantId, "hash", DateTime.UtcNow.AddDays(1));
        var tokens = Substitute.For<IEmailDecisionTokenRepository>();
        tokens.GetByTokenHashAsync("hash", Arg.Any<CancellationToken>()).Returns(Result.Success(token));
        var requests = Substitute.For<IApprovalRequestRepository>();
        requests.GetByTaskIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var generator = Substitute.For<IEmailTokenGenerator>();
        generator.Hash("raw").Returns("hash");
        var handler = new DecideByEmailTokenHandler(tokens, requests, generator, Substitute.For<IIntegrationEventOutbox>(), TimeProvider.System);

        var result = await handler.Handle(new DecideByEmailTokenCommand("raw", ApprovalOutcome.Rejected, "Uygun değil", null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalRequestStatus.Rejected);
    }
}
