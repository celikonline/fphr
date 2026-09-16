using System.IdentityModel.Tokens.Jwt;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application.Impersonation;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Faz 0 item 11: platform staff reach tenant data only through an impersonation session backed by an Active, unexpired,
/// unrevoked grant. The session principal is built from the issued JWT exactly like RequestContextMiddleware does.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PlatformImpersonationTests : IdentityStackTestBase
{
    [Fact]
    public async Task FullFlow_RequestApproveStartActRevoke_AndExpiry()
    {
        var platformTenant = await SeedTenantAsync("sense-platform");
        var staffId = await SeedUserAsync(platformTenant, "support@sense.test", SystemRoleCodes.Owner, platformStaff: true);

        var customer = await SeedTenantAsync("customer");
        var ownerId = await SeedUserAsync(customer, "owner@customer.test", SystemRoleCodes.Owner);
        var employee = await SeedEmployeeAsync(customer, "Gizli", "Veri");

        // Without any grant a forged platform principal (platform_admin and/or act claims) gets nothing.
        ActAs(customer, PlatformPrincipal(staffId, impersonating: true));
        ShouldFailWith(await ExecuteQueryAsync(ListAll()), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ActAs(customer, PlatformPrincipal(staffId, impersonating: false));
        ShouldFailWith(await ExecuteQueryAsync(ListAll()), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteQueryAsync(new GetAccessSettingsQuery()), ErrorCodes.Forbidden, ErrorType.Forbidden);
        (await Get<IDataScopeService>().GetVisibleEmployeeIdsAsync(EmployeePermissions.EmployeesReadKey, Ct)).ShouldBeEmpty();

        ActAs(platformTenant, staffId);
        var grantId = (await ExecuteCommandAsync(new RequestImpersonationCommand(customer, "Ticket #42"))).Value;

        // Requested but not approved: still no access, and start is refused.
        ShouldFailWith(await ExecuteCommandAsync(new StartImpersonationCommand(customer, grantId)), "impersonation.not_approved", ErrorType.Rule);
        ActAs(customer, PlatformPrincipal(staffId, impersonating: true));
        ShouldFailWith(await ExecuteQueryAsync(ListAll()), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(customer, ownerId);
        var pending = (await ExecuteQueryAsync(new ListImpersonationGrantsQuery("pending"))).Value.ShouldHaveSingleItem();
        pending.Id.ShouldBe(grantId);
        pending.Status.ShouldBe("Pending");
        pending.PlatformUserEmail.ShouldBe("support@sense.test");
        (await ExecuteCommandAsync(new ApproveImpersonationCommand(grantId))).IsSuccess.ShouldBeTrue();

        ActAs(platformTenant, staffId);
        var session = await ExecuteCommandAsync(new StartImpersonationCommand(customer, grantId));
        session.IsSuccess.ShouldBeTrue(session.IsFailure ? session.Error.Code : null);

        ActAs(customer, FromToken(session.Value.AccessToken));
        (await ExecuteQueryAsync(ListAll())).Value.Items.ShouldHaveSingleItem().Id.ShouldBe(employee);
        (await ExecuteQueryAsync(new GetAccessSettingsQuery())).IsSuccess.ShouldBeTrue();

        ActAs(customer, ownerId);
        var active = (await ExecuteQueryAsync(new ListImpersonationGrantsQuery(null))).Value.ShouldHaveSingleItem();
        active.Status.ShouldBe("Active");
        active.ApprovedAt.ShouldNotBeNull();
        active.StartedAt.ShouldNotBeNull();
        (await ExecuteCommandAsync(new RevokeImpersonationCommand(grantId))).IsSuccess.ShouldBeTrue();

        // The already-issued token is dead the moment the grant is revoked.
        ActAs(customer, FromToken(session.Value.AccessToken));
        ShouldFailWith(await ExecuteQueryAsync(ListAll()), ErrorCodes.Forbidden, ErrorType.Forbidden);

        // Expiry: a second, approved and started grant stops working once its window has passed.
        ActAs(platformTenant, staffId);
        var secondGrant = (await ExecuteCommandAsync(new RequestImpersonationCommand(customer, "Ticket #43"))).Value;
        ActAs(customer, ownerId);
        (await ExecuteCommandAsync(new ApproveImpersonationCommand(secondGrant))).IsSuccess.ShouldBeTrue();
        ActAs(platformTenant, staffId);
        var secondSession = (await ExecuteCommandAsync(new StartImpersonationCommand(customer, secondGrant))).Value;
        ActAs(customer, FromToken(secondSession.AccessToken));
        (await ExecuteQueryAsync(ListAll())).IsSuccess.ShouldBeTrue();

        Time.Advance(TimeSpan.FromHours(5));
        ShouldFailWith(await ExecuteQueryAsync(ListAll()), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(customer, ownerId);
        var statuses = (await ExecuteQueryAsync(new ListImpersonationGrantsQuery(null))).Value.ToDictionary(g => g.Id, g => g.Status);
        statuses[grantId].ShouldBe("Revoked");
        statuses[secondGrant].ShouldBe("Expired");
        (await ExecuteQueryAsync(new ListImpersonationGrantsQuery("expired"))).Value.ShouldHaveSingleItem().Id.ShouldBe(secondGrant);
    }

    [Fact]
    public async Task GrantInOtherTenant_DoesNotOpenThisTenant_AndTenantUsersCannotListGrantsWithoutApprovePermission()
    {
        var platformTenant = await SeedTenantAsync("sense-platform-2");
        var staffId = await SeedUserAsync(platformTenant, "support2@sense.test", SystemRoleCodes.Owner, platformStaff: true);
        var tenantA = await SeedTenantAsync("tenant-a");
        var ownerA = await SeedUserAsync(tenantA, "owner@a.test", SystemRoleCodes.Owner);
        var employeeA = await SeedUserAsync(tenantA, "plain@a.test", SystemRoleCodes.Employee);
        var tenantB = await SeedTenantAsync("tenant-b");

        ActAs(platformTenant, staffId);
        var grant = (await ExecuteCommandAsync(new RequestImpersonationCommand(tenantA, "Ticket"))).Value;
        ActAs(tenantA, ownerA);
        (await ExecuteCommandAsync(new ApproveImpersonationCommand(grant))).IsSuccess.ShouldBeTrue();
        ActAs(platformTenant, staffId);
        var token = (await ExecuteCommandAsync(new StartImpersonationCommand(tenantA, grant))).Value.AccessToken;

        // Same principal pointed at another tenant: no grant there.
        ActAs(tenantB, FromToken(token));
        ShouldFailWith(await ExecuteQueryAsync(new GetAccessSettingsQuery()), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(tenantA, employeeA);
        ShouldFailWith(await ExecuteQueryAsync(new ListImpersonationGrantsQuery(null)), ErrorCodes.Forbidden, ErrorType.Forbidden);
    }

    private static ListEmployeesQuery ListAll() =>
        new(new PagedQuery { PageSize = 100 }, new EmployeeFilter(null, null, false, null, null, null));

    private static AuthenticatedUser PlatformPrincipal(Guid userId, bool impersonating) => new(
        UserId: userId, EmployeeId: null, Email: "support@sense.test", DisplayName: "Support",
        Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: true,
        ImpersonatorUserId: impersonating ? userId : null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1");

    /// <summary>Same claim mapping as RequestContextMiddleware.</summary>
    private static AuthenticatedUser FromToken(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        string? Claim(string type) => jwt.Claims.FirstOrDefault(c => c.Type == type)?.Value;
        return new AuthenticatedUser(
            UserId: Guid.Parse(Claim(ClaimNames.Subject)!),
            EmployeeId: Guid.TryParse(Claim(ClaimNames.Employee), out var eid) ? eid : null,
            Email: Claim(ClaimNames.Email),
            DisplayName: Claim(ClaimNames.Name),
            Roles: jwt.Claims.Where(c => c.Type == ClaimNames.Roles).Select(c => c.Value).ToHashSet(StringComparer.Ordinal),
            IsPlatformAdmin: Claim(ClaimNames.PlatformAdmin) == ClaimNames.TrueValue,
            ImpersonatorUserId: Guid.TryParse(Claim(ClaimNames.Impersonator), out var act) ? act : null,
            ApiKeyId: null,
            CorrelationId: null,
            IpAddress: "127.0.0.1");
    }
}
