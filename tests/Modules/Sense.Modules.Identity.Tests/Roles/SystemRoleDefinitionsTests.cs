using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Roles;

public sealed class SystemRoleDefinitionsTests
{
    private static readonly IReadOnlyList<Permission> Catalog =
    [
        .. new[]
        {
            "employee.employees.read", "employee.employees.create", "employee.employees.update", "employee.employees.bulk_update",
            "employee.directory.view", "employee.orgchart.view", "employee.salary.view", "employee.salary.edit", "employee.bank_accounts.edit",
            "employee.sensitive.view", "organization.holidays.view", "organization.units.read",
            "organization.approval_assignments.manage", "billing.invoices.view",
        }.Select(k => new Permission(k, k.Split('.')[0], k, "Test")),
    ];

    [Theory]
    [InlineData(SystemRoleCodes.Employee)]
    [InlineData(SystemRoleCodes.Intern)]
    public void SelfServiceRoles_GetDirectoryOrgChartAndHolidays_ButNotEmployeeRecords(string roleCode)
    {
        var keys = Resolve(roleCode);

        keys.ShouldBe(["employee.directory.view", "employee.orgchart.view", "organization.holidays.view"], ignoreOrder: true);
    }

    [Fact]
    public void HRSpecialist_GetsEmployeeWrites_ButNotSalary()
    {
        var keys = Resolve(SystemRoleCodes.HRSpecialist);

        keys.ShouldContain("employee.employees.update");
        keys.ShouldContain("employee.employees.bulk_update");
        keys.ShouldNotContain("employee.salary.view");
    }

    [Fact]
    public void Manager_GetsViews_ButNotSalary()
    {
        var keys = Resolve(SystemRoleCodes.Manager);

        keys.ShouldContain("employee.directory.view");
        keys.ShouldContain("employee.orgchart.view");
        keys.ShouldNotContain("employee.salary.view");
    }

    [Fact]
    public void SalaryEditAndBankAccounts_GoToHrAndPayroll_ButSalaryNeverToHRSpecialistOrManager()
    {
        Resolve(SystemRoleCodes.HR).ShouldContain("employee.salary.edit");
        Resolve(SystemRoleCodes.HR).ShouldContain("employee.bank_accounts.edit");
        Resolve(SystemRoleCodes.PayrollSpecialist).ShouldContain("employee.salary.edit");
        Resolve(SystemRoleCodes.PayrollSpecialist).ShouldContain("employee.bank_accounts.edit");
        Resolve(SystemRoleCodes.HRSpecialist).ShouldNotContain("employee.salary.edit");
        Resolve(SystemRoleCodes.HRSpecialist).ShouldContain("employee.bank_accounts.edit");
        Resolve(SystemRoleCodes.Manager).ShouldNotContain("employee.salary.edit");
        Resolve(SystemRoleCodes.Manager).ShouldNotContain("employee.bank_accounts.edit");
        Resolve(SystemRoleCodes.Auditor).ShouldNotContain("employee.sensitive.view");
    }

    [Fact]
    public void Owner_And_Admin_BothGetBilling()
    {
        // Faz 8 #103: the source transcript has admins reaching billing while owners cannot — the plan direction
        // (Owner keeps billing.*) was kept, but Admin's exclusion of it was lifted so both types get it now.
        Resolve(SystemRoleCodes.Owner).ShouldContain("billing.invoices.view");
        Resolve(SystemRoleCodes.Admin).ShouldContain("billing.invoices.view");
    }

    [Fact]
    public void UnitApprovalPolicyAssignment_IsGrantedToHR_ButNotToHRSpecialistOrManager()
    {
        Resolve(SystemRoleCodes.HR).ShouldContain("organization.approval_assignments.manage");
        Resolve(SystemRoleCodes.HRSpecialist).ShouldNotContain("organization.approval_assignments.manage");
        Resolve(SystemRoleCodes.Manager).ShouldNotContain("organization.approval_assignments.manage");
    }

    private static List<string> Resolve(string roleCode) =>
        SystemRoleDefinitions.Get(roleCode).Resolve(Guid.NewGuid(), Catalog).Select(p => p.Permission).ToList();
}
