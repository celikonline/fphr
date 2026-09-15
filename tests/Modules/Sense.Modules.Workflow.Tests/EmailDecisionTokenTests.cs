using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>Pure domain tests for EmailDecisionToken.MarkUsed - single-use, expiring token semantics.</summary>
public sealed class EmailDecisionTokenTests
{
    [Fact]
    public void MarkUsed_ValidUnexpiredUnusedToken_SucceedsAndSetsUsedAt()
    {
        var now = DateTime.UtcNow;
        var token = new EmailDecisionToken(Guid.NewGuid(), Guid.NewGuid(), "hash", now.AddDays(1));

        var result = token.MarkUsed(now);

        result.IsSuccess.ShouldBeTrue();
        token.UsedAt.ShouldBe(now);
    }

    [Fact]
    public void MarkUsed_CalledAgainOnSameToken_Fails_TokenAlreadyUsed()
    {
        var now = DateTime.UtcNow;
        var token = new EmailDecisionToken(Guid.NewGuid(), Guid.NewGuid(), "hash", now.AddDays(1));
        token.MarkUsed(now).IsSuccess.ShouldBeTrue();

        var result = token.MarkUsed(now.AddMinutes(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.TokenAlreadyUsed);
    }

    [Fact]
    public void MarkUsed_PastExpiresAt_Fails_TokenExpired()
    {
        var expiresAt = DateTime.UtcNow;
        var token = new EmailDecisionToken(Guid.NewGuid(), Guid.NewGuid(), "hash", expiresAt);

        var result = token.MarkUsed(expiresAt.AddSeconds(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.TokenExpired);
        token.UsedAt.ShouldBeNull();
    }
}
