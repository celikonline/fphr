using NSubstitute;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Security;

/// <summary>The platform_admin/act claims alone never bypass tenant authorization; only an active impersonation session does.</summary>
public sealed class AuthorizationBehaviourPlatformTests
{
    [RequiresPermission("test.things.read")]
    public sealed record GuardedQuery : IQuery<int>;

    public sealed record UnguardedQuery : IQuery<int>;

    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IDataScopeService _scope = Substitute.For<IDataScopeService>();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PlatformPrincipal_WithoutValidator_IsForbidden_EvenForUnguardedRequests(bool platformAdmin, bool impersonating)
    {
        var user = Principal(platformAdmin, impersonating);

        (await Run<GuardedQuery>(user, validator: null)).Error.Code.ShouldBe(ErrorCodes.Forbidden);
        (await Run<UnguardedQuery>(user, validator: null)).Error.Code.ShouldBe(ErrorCodes.Forbidden);
        await _permissions.DidNotReceiveWithAnyArgs().HasAsync(default, default!, default);
    }

    [Fact]
    public async Task PlatformPrincipal_WithInactiveSession_IsForbidden_AndWithActiveSession_Passes()
    {
        var user = Principal(platformAdmin: true, impersonating: true);
        var sessions = Substitute.For<IImpersonationSessionValidator>();

        sessions.IsActiveAsync(Arg.Any<CancellationToken>()).Returns(false);
        (await Run<GuardedQuery>(user, sessions)).Error.Code.ShouldBe(ErrorCodes.Forbidden);

        sessions.IsActiveAsync(Arg.Any<CancellationToken>()).Returns(true);
        (await Run<GuardedQuery>(user, sessions)).Value.ShouldBe(42);
    }

    [Fact]
    public async Task RegularUser_StillGoesThroughPermissions()
    {
        var user = Principal(platformAdmin: false, impersonating: false);
        _permissions.HasAsync(user.UserId, "test.things.read", Arg.Any<CancellationToken>()).Returns(false);

        (await Run<GuardedQuery>(user, validator: null)).Error.Code.ShouldBe(ErrorCodes.Forbidden);
        (await Run<UnguardedQuery>(user, validator: null)).Value.ShouldBe(42);

        _permissions.HasAsync(user.UserId, "test.things.read", Arg.Any<CancellationToken>()).Returns(true);
        (await Run<GuardedQuery>(user, validator: null)).Value.ShouldBe(42);
    }

    [Fact]
    public async Task SystemContext_WithoutUserId_IsNotAPlatformBypass()
    {
        (await Run<GuardedQuery>(SystemUser.Instance, validator: null)).Error.Code.ShouldBe(ErrorCodes.Unauthenticated);
        (await Run<UnguardedQuery>(SystemUser.Instance, validator: null)).Value.ShouldBe(42);
    }

    private Task<Result<int>> Run<TRequest>(Sense.Shared.Contracts.Context.ICurrentUser user, IImpersonationSessionValidator? validator)
        where TRequest : notnull, new()
    {
        var behaviour = new AuthorizationBehaviour<TRequest, Result<int>>(user, _permissions, _scope, validator);
        return behaviour.Handle(new TRequest(), () => Task.FromResult(Result.Success(42)), CancellationToken.None);
    }

    private static AuthenticatedUser Principal(bool platformAdmin, bool impersonating)
    {
        var id = Guid.NewGuid();
        return new AuthenticatedUser(id, null, "x@test.local", "X", new HashSet<string>(StringComparer.Ordinal), platformAdmin,
            impersonating ? id : null, null, null, null);
    }
}
