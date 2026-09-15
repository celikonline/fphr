using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.UnitAuthorizations;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.UnitAuthorizations;

/// <summary>Domain düzeyinde: Birim yetkilendirme grant/upsert/revoke kuralları (Faz 0 madde 3).</summary>
public sealed class UnitAuthorizationTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid OrgUnitId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid GrantedByUserId = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Grant_with_at_least_one_key_succeeds()
    {
        var result = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, canViewSalaryAndPayments: true, canViewUnitReports: false, GrantedByUserId, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OrgUnitId.ShouldBe(OrgUnitId);
        result.Value.UserId.ShouldBe(UserId);
        result.Value.CanViewSalaryAndPayments.ShouldBeTrue();
        result.Value.CanViewUnitReports.ShouldBeFalse();
        result.Value.GrantedByUserId.ShouldBe(GrantedByUserId);
        result.Value.GrantedAt.ShouldBe(Now);
        result.Value.IsActive.ShouldBeTrue();
        result.Value.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public void Grant_with_neither_key_selected_fails()
    {
        var result = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, canViewSalaryAndPayments: false, canViewUnitReports: false, GrantedByUserId, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityDomainErrors.UnitAuthorizationNoGrantsSelected);
    }

    [Fact]
    public void Revoke_deactivates_and_sets_revoked_at()
    {
        var grant = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, canViewSalaryAndPayments: false, canViewUnitReports: true, GrantedByUserId, Now).Value;

        var revoked = grant.Revoke(Now.AddDays(1));

        revoked.IsSuccess.ShouldBeTrue();
        grant.IsActive.ShouldBeFalse();
        grant.RevokedAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void Revoke_twice_fails()
    {
        var grant = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, canViewSalaryAndPayments: false, canViewUnitReports: true, GrantedByUserId, Now).Value;
        grant.Revoke(Now);

        var secondRevoke = grant.Revoke(Now);

        secondRevoke.IsFailure.ShouldBeTrue();
        secondRevoke.Error.Code.ShouldBe(IdentityDomainErrors.UnitAuthorizationAlreadyRevoked);
    }

    [Fact]
    public void UpdateGrants_on_active_grant_replaces_keys_and_granted_metadata()
    {
        var grant = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, canViewSalaryAndPayments: true, canViewUnitReports: false, GrantedByUserId, Now).Value;
        var otherAdmin = Guid.CreateVersion7();

        var updated = grant.UpdateGrants(canViewSalaryAndPayments: false, canViewUnitReports: true, otherAdmin, Now.AddDays(2));

        updated.IsSuccess.ShouldBeTrue();
        grant.CanViewSalaryAndPayments.ShouldBeFalse();
        grant.CanViewUnitReports.ShouldBeTrue();
        grant.GrantedByUserId.ShouldBe(otherAdmin);
        grant.GrantedAt.ShouldBe(Now.AddDays(2));
        grant.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void UpdateGrants_on_revoked_grant_fails()
    {
        var grant = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, canViewSalaryAndPayments: true, canViewUnitReports: false, GrantedByUserId, Now).Value;
        grant.Revoke(Now);

        var updated = grant.UpdateGrants(true, true, GrantedByUserId, Now.AddDays(1));

        updated.IsFailure.ShouldBeTrue();
        updated.Error.Code.ShouldBe(IdentityDomainErrors.UnitAuthorizationAlreadyRevoked);
    }

    [Fact]
    public void UpdateGrants_with_neither_key_selected_fails()
    {
        var grant = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, canViewSalaryAndPayments: true, canViewUnitReports: false, GrantedByUserId, Now).Value;

        var updated = grant.UpdateGrants(false, false, GrantedByUserId, Now.AddDays(1));

        updated.IsFailure.ShouldBeTrue();
        updated.Error.Code.ShouldBe(IdentityDomainErrors.UnitAuthorizationNoGrantsSelected);
    }

    [Fact]
    public void Same_user_can_be_granted_on_multiple_different_org_units()
    {
        var otherOrgUnitId = Guid.CreateVersion7();

        var firstGrant = UnitAuthorization.Grant(TenantId, OrgUnitId, UserId, true, false, GrantedByUserId, Now);
        var secondGrant = UnitAuthorization.Grant(TenantId, otherOrgUnitId, UserId, false, true, GrantedByUserId, Now);

        firstGrant.IsSuccess.ShouldBeTrue();
        secondGrant.IsSuccess.ShouldBeTrue();
        firstGrant.Value.Id.ShouldNotBe(secondGrant.Value.Id);
        firstGrant.Value.OrgUnitId.ShouldNotBe(secondGrant.Value.OrgUnitId);
    }
}
