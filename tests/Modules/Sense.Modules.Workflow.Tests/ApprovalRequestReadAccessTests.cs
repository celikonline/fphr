using NSubstitute;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

public sealed class ApprovalRequestReadAccessTests
{
    [Theory]
    [InlineData("requester", true)]
    [InlineData("approver", true)]
    [InlineData("decider", true)]
    [InlineData("fallback", true)]
    [InlineData("override", true)]
    [InlineData("inbox-only", false)]
    [InlineData("foreign-tenant", false)]
    [InlineData("foreign-subject", false)]
    [InlineData("wrong-type", false)]
    public async Task CanRead_RequiresMatchingSubjectAndExistingPartyRule(string actor, bool allowed)
    {
        var tenantId = Guid.NewGuid();
        var requester = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var decider = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var caller = actor switch
        {
            "requester" or "foreign-tenant" or "foreign-subject" or "wrong-type" => requester,
            "approver" => approver,
            "decider" => decider,
            _ => Guid.NewGuid(),
        };
        var request = ApprovalRequest.Submit(tenantId, "ExpenseClaim", Guid.NewGuid(), requester,
            [new(1, approver, null, true), new(2, null, "HR", true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;
        request.Decide(1, decider, ApprovalOutcome.Approved, "approved", true, ApprovalChannel.Web, null, null, DateTime.UtcNow);
        var repository = Substitute.For<IApprovalRequestRepository>();
        repository.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(request));
        var roles = Substitute.For<IRoleAssignmentLookup>();
        roles.EmployeeHasRoleAsync(tenantId, caller, "HR", Arg.Any<CancellationToken>()).Returns(actor == "fallback");
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(userId, "workflow.request.override", Arg.Any<CancellationToken>()).Returns(actor == "override");
        permissions.HasAsync(userId, "workflow.inbox.view", Arg.Any<CancellationToken>()).Returns(true);
        var result = await new ApprovalRequestReadAccess(repository, roles, permissions).CanReadAsync(
            actor == "foreign-tenant" ? Guid.NewGuid() : tenantId, request.Id,
            actor == "wrong-type" ? "Advance" : "ExpenseClaim", actor == "foreign-subject" ? Guid.NewGuid() : request.SubjectId,
            caller, userId, CancellationToken.None);
        result.ShouldBe(allowed);
    }
}
