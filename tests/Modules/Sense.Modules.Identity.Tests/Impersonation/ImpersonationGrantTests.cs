using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Impersonation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Impersonation;

public sealed class ImpersonationGrantTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid PlatformUserId = Guid.CreateVersion7();
    private static readonly Guid ApproverId = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_starts_as_pending()
    {
        var result = ImpersonationGrant.Create(TenantId, PlatformUserId, "onboarding support ticket #123", Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ImpersonationGrantStatus.Pending);
        result.Value.PlatformUserId.ShouldBe(PlatformUserId);
        result.Value.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void Approve_opens_the_usage_window()
    {
        var grant = ImpersonationGrant.Create(TenantId, PlatformUserId, "reason", Now).Value;

        var approved = grant.Approve(ApproverId, Now, TimeSpan.FromHours(4));

        approved.IsSuccess.ShouldBeTrue();
        grant.Status.ShouldBe(ImpersonationGrantStatus.Approved);
        grant.ApprovedByUserId.ShouldBe(ApproverId);
        grant.ExpiresAt.ShouldBe(Now.AddHours(4));
    }

    [Fact]
    public void Approve_twice_fails()
    {
        var grant = ImpersonationGrant.Create(TenantId, PlatformUserId, "reason", Now).Value;
        grant.Approve(ApproverId, Now, TimeSpan.FromHours(4));

        var secondApprove = grant.Approve(ApproverId, Now, TimeSpan.FromHours(4));

        secondApprove.IsFailure.ShouldBeTrue();
        secondApprove.Error.Code.ShouldBe(IdentityDomainErrors.ImpersonationNotPending);
    }

    [Fact]
    public void Start_without_approval_fails()
    {
        var grant = ImpersonationGrant.Create(TenantId, PlatformUserId, "reason", Now).Value;

        var started = grant.Start(Now);

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe(IdentityDomainErrors.ImpersonationNotApproved);
    }

    [Fact]
    public void Start_within_window_succeeds_once()
    {
        var grant = ImpersonationGrant.Create(TenantId, PlatformUserId, "reason", Now).Value;
        grant.Approve(ApproverId, Now, TimeSpan.FromHours(4));

        var started = grant.Start(Now.AddMinutes(30));

        started.IsSuccess.ShouldBeTrue();
        grant.Status.ShouldBe(ImpersonationGrantStatus.Active);
        grant.UsedAt.ShouldBe(Now.AddMinutes(30));

        // Bir kez kullanılır: ikinci Start çağrısı artık Approved olmadığı için reddedilir.
        var secondStart = grant.Start(Now.AddMinutes(31));
        secondStart.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Start_after_the_window_expires_and_fails()
    {
        var grant = ImpersonationGrant.Create(TenantId, PlatformUserId, "reason", Now).Value;
        grant.Approve(ApproverId, Now, TimeSpan.FromHours(4));

        var started = grant.Start(Now.AddHours(5));

        started.IsFailure.ShouldBeTrue();
        started.Error.Code.ShouldBe(IdentityDomainErrors.ImpersonationExpired);
        grant.Status.ShouldBe(ImpersonationGrantStatus.Expired);
    }

    [Fact]
    public void Revoke_from_pending_succeeds_but_not_twice()
    {
        var grant = ImpersonationGrant.Create(TenantId, PlatformUserId, "reason", Now).Value;

        grant.Revoke(Now).IsSuccess.ShouldBeTrue();
        grant.Status.ShouldBe(ImpersonationGrantStatus.Revoked);

        grant.Revoke(Now).IsFailure.ShouldBeTrue();
    }
}
