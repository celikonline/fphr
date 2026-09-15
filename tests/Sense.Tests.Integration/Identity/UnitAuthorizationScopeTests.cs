using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.UnitAuthorizations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>Faz 0 item 3: an Employee-role user authorized on a unit manages that unit's subtree only.</summary>
[Collection("PostgreSQL")]
public sealed class UnitAuthorizationScopeTests : IdentityStackTestBase
{
    [Fact]
    public async Task UnitManager_SeesAndCreatesOnlyInAuthorizedSubtree_UntilRevoked()
    {
        var tenantId = await SeedTenantAsync("unit-auth");
        var tree = await SeedOrgTreeAsync(tenantId);
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);

        var salesLead = await SeedEmployeeAsync(tenantId, "Selin", "Satış", tree.Sales);
        var emeaRep = await SeedEmployeeAsync(tenantId, "Emre", "Emea", tree.SalesEmea);
        var accountant = await SeedEmployeeAsync(tenantId, "Fatma", "Finans", tree.Finance);
        var managerRecord = await SeedEmployeeAsync(tenantId, "Mert", "Yönetici", tree.Root);
        var managerUserId = await SeedUserAsync(tenantId, "mert@acme.test", SystemRoleCodes.Employee, managerRecord);

        // Before any grant: a plain employee cannot list employees at all.
        ActAs(tenantId, managerUserId, managerRecord);
        ShouldFailWith(await ExecuteQueryAsync(ListAll()), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(tenantId, ownerId);
        var grantId = (await ExecuteCommandAsync(new GrantUnitAuthorizationCommand(tree.Sales, managerUserId, false, true))).Value;

        var listed = (await ExecuteQueryAsync(new ListUnitAuthorizationsQuery(tree.Sales))).Value.ShouldHaveSingleItem();
        listed.UserEmail.ShouldBe("mert@acme.test");
        listed.UserFullName.ShouldBe("mert Test");
        listed.EmployeeId.ShouldBe(managerRecord);
        (await ExecuteQueryAsync(new ListUnitAuthorizationCandidatesQuery("MERT"))).Value.ShouldHaveSingleItem().UserId.ShouldBe(managerUserId);

        ActAs(tenantId, managerUserId, managerRecord);
        var visible = (await ExecuteQueryAsync(ListAll())).Value.Items.Select(e => e.Id).ToList();
        visible.ShouldBe([salesLead, emeaRep, managerRecord], ignoreOrder: true);
        visible.ShouldNotContain(accountant);

        (await ExecuteQueryAsync(new GetEmployeeQuery(emeaRep))).IsSuccess.ShouldBeTrue();
        ShouldFailWith(await ExecuteQueryAsync(new GetEmployeeQuery(accountant)), ErrorCodes.OutOfScope, ErrorType.NotFound);

        var hired = await ExecuteCommandAsync(NewEmployee("Yeni", "Emea", tree.SalesEmea));
        hired.IsSuccess.ShouldBeTrue(hired.IsFailure ? hired.Error.Code : null);
        ShouldFailWith(await ExecuteCommandAsync(NewEmployee("Yanlış", "Birim", tree.Finance)), EmployeeErrors.OrgUnitOutOfScope, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(NewEmployee("Birimsiz", "Kişi", null)), EmployeeErrors.OrgUnitOutOfScope, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new UpdateEmployeeCommand(emeaRep, "Emre", "Emea", null, null, null, null, null, null, tree.Finance, null)),
            EmployeeErrors.OrgUnitOutOfScope, ErrorType.Forbidden);
        (await ExecuteCommandAsync(new UpdateEmployeeCommand(emeaRep, "Emre Can", "Emea", null, null, null, null, null, null, tree.Sales, null))).IsSuccess.ShouldBeTrue();

        var me = (await ExecuteQueryAsync(new GetMeQuery())).Value;
        me.Permissions.ShouldContain(EmployeePermissions.EmployeesCreateKey);
        me.Permissions.ShouldContain(UnitManagerGrants.UnitReportsRun);
        me.Permissions.ShouldNotContain(EmployeePermissions.SalaryViewKey);
        me.Permissions.ShouldNotContain(UnitManagerGrants.CustomReportBuild);
        me.Scopes[EmployeePermissions.EmployeesReadKey].ShouldBe(DataScope.Department);

        var permissions = Get<IPermissionService>();
        (await permissions.HasAsync(managerUserId, EmployeePermissions.SalaryViewKey, Ct)).ShouldBeFalse();

        // Salary switch toggles employee.salary.view (cache invalidated by the grant command).
        ActAs(tenantId, ownerId);
        (await ExecuteCommandAsync(new GrantUnitAuthorizationCommand(tree.Sales, managerUserId, true, true))).Value.ShouldBe(grantId);
        ActAs(tenantId, managerUserId, managerRecord);
        (await permissions.HasAsync(managerUserId, EmployeePermissions.SalaryViewKey, Ct)).ShouldBeTrue();
        ActAs(tenantId, ownerId);
        (await ExecuteCommandAsync(new GrantUnitAuthorizationCommand(tree.Sales, managerUserId, false, true))).IsSuccess.ShouldBeTrue();
        ActAs(tenantId, managerUserId, managerRecord);
        (await permissions.HasAsync(managerUserId, EmployeePermissions.SalaryViewKey, Ct)).ShouldBeFalse();

        // An inactive sub-unit drops out of the subtree immediately.
        await DeactivateUnitAsync(tenantId, tree.SalesEmea);
        var afterDeactivation = (await ExecuteQueryAsync(ListAll())).Value.Items.Select(e => e.Id).ToList();
        afterDeactivation.ShouldNotContain(hired.Value.Id);
        afterDeactivation.ShouldContain(emeaRep);

        ActAs(tenantId, ownerId);
        (await ExecuteCommandAsync(new RevokeUnitAuthorizationCommand(grantId))).IsSuccess.ShouldBeTrue();

        ActAs(tenantId, managerUserId, managerRecord);
        ShouldFailWith(await ExecuteQueryAsync(ListAll()), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteQueryAsync(new GetEmployeeQuery(salesLead)), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(NewEmployee("Geç", "Kaldı", tree.Sales)), ErrorCodes.Forbidden, ErrorType.Forbidden);
    }

    [Fact]
    public async Task CompanyScopedRole_IsNotRestrictedByUnits_AndCandidatesRequireManagePermission()
    {
        var tenantId = await SeedTenantAsync("unit-auth-hr");
        var tree = await SeedOrgTreeAsync(tenantId);
        var hrId = await SeedUserAsync(tenantId, "hr@acme.test", SystemRoleCodes.HR);
        var employeeUserId = await SeedUserAsync(tenantId, "plain@acme.test", SystemRoleCodes.Employee);

        ActAs(tenantId, hrId);
        (await Get<IDataScopeService>().GetAccessibleOrgUnitIdsAsync(EmployeePermissions.EmployeesCreateKey, Ct)).ShouldBeNull();
        (await ExecuteCommandAsync(NewEmployee("Her", "Yer", tree.Finance))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(NewEmployee("Birim", "Yok", null))).IsSuccess.ShouldBeTrue();

        ActAs(tenantId, employeeUserId);
        ShouldFailWith(await ExecuteQueryAsync(new ListUnitAuthorizationCandidatesQuery(null)), ErrorCodes.Forbidden, ErrorType.Forbidden);
        (await Get<IDataScopeService>().GetAccessibleOrgUnitIdsAsync(EmployeePermissions.EmployeesCreateKey, Ct)).ShouldBeEmpty();
    }

    private static ListEmployeesQuery ListAll() =>
        new(new PagedQuery { PageSize = 100 }, new EmployeeFilter(null, null, false, null, null, null));

    private static CreateEmployeeCommand NewEmployee(string firstName, string lastName, Guid? orgUnitId) =>
        new(firstName, lastName, null, null, null, null, null, null, orgUnitId, null);

    private async Task DeactivateUnitAsync(Guid tenantId, Guid unitId)
    {
        var db = Get<OrganizationDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var unit = db.OrgUnits.Single(u => u.Id == unitId);
            unit.Deactivate().IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }
    }
}
