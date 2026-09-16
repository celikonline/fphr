using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>Faz 0 item 1: access settings restrict plain employees only; Owner/Admin/HR bypass; directory phone masking; /me effective features.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AccessSettingsEnforcementTests : IdentityStackTestBase
{
    [Fact]
    public async Task OrgChartAndBirthdays_AreGatedForEmployees_ButBypassedByAdminAndHr()
    {
        var tenantId = await SeedTenantAsync("access-gate");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        var adminId = await SeedUserAsync(tenantId, "admin@acme.test", SystemRoleCodes.Admin);
        var hrId = await SeedUserAsync(tenantId, "hr@acme.test", SystemRoleCodes.HR);
        var employeeRecord = await SeedEmployeeAsync(tenantId, "Ece", "Çalışan", workPhone: "+90 212 000 00 01");
        var employeeUserId = await SeedUserAsync(tenantId, "ece@acme.test", SystemRoleCodes.Employee, employeeRecord);

        await SetAccessAsync(tenantId, ownerId, showOrgChart: false, showBirthdays: false, showPhones: false);

        ActAs(tenantId, employeeUserId, employeeRecord);
        ShouldFailWith(await ExecuteQueryAsync(new GetOrgChartQuery()), ErrorCodes.FeatureDisabled, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteQueryAsync(new GetUpcomingBirthdaysQuery()), ErrorCodes.FeatureDisabled, ErrorType.Forbidden);
        var masked = (await ExecuteQueryAsync(new GetEmployeeDirectoryQuery(new PagedQuery(), null))).Value;
        masked.Items.ShouldHaveSingleItem().WorkPhone.ShouldBeNull();

        var me = (await ExecuteQueryAsync(new GetMeQuery())).Value;
        me.Features.AccessSettingsBypass.ShouldBeFalse();
        me.Features.Access.ShowOrgChart.ShouldBeFalse();
        me.Features.Access.ShowWorkPhoneNumbers.ShouldBeFalse();
        me.Features.Access.ShowOrgInfo.ShouldBeTrue();

        foreach (var privileged in new[] { adminId, hrId })
        {
            ActAs(tenantId, privileged);
            (await ExecuteQueryAsync(new GetOrgChartQuery())).IsSuccess.ShouldBeTrue();
            (await ExecuteQueryAsync(new GetUpcomingBirthdaysQuery())).IsSuccess.ShouldBeTrue();
            var unmasked = (await ExecuteQueryAsync(new GetEmployeeDirectoryQuery(new PagedQuery(), null))).Value;
            unmasked.Items.ShouldHaveSingleItem().WorkPhone.ShouldBe("+90 212 000 00 01");

            var privilegedMe = (await ExecuteQueryAsync(new GetMeQuery())).Value;
            privilegedMe.Features.AccessSettingsBypass.ShouldBeTrue();
            privilegedMe.Features.Access.ShouldBe(AccessSettingsDto.AllEnabled);
        }

        await SetAccessAsync(tenantId, ownerId, showOrgChart: true, showBirthdays: true, showPhones: true);

        ActAs(tenantId, employeeUserId, employeeRecord);
        (await ExecuteQueryAsync(new GetOrgChartQuery())).IsSuccess.ShouldBeTrue();
        (await ExecuteQueryAsync(new GetUpcomingBirthdaysQuery())).IsSuccess.ShouldBeTrue();
        (await ExecuteQueryAsync(new GetEmployeeDirectoryQuery(new PagedQuery(), null))).Value.Items.ShouldHaveSingleItem().WorkPhone.ShouldBe("+90 212 000 00 01");
        (await ExecuteQueryAsync(new GetMeQuery())).Value.Features.Access.ShowOrgChart.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdatingAccessSettings_RequiresTenantManage()
    {
        var tenantId = await SeedTenantAsync("access-manage");
        var employeeUserId = await SeedUserAsync(tenantId, "plain@acme.test", SystemRoleCodes.Employee);

        ActAs(tenantId, employeeUserId);
        var update = AccessSettingsDto.AllEnabled;
        ShouldFailWith(
            await ExecuteCommandAsync(new UpdateAccessSettingsCommand(update.ShowOrgInfo, update.ShowUpcomingLeaves, update.ShowUpcomingBirthdays, update.ShowEventCalendar,
                update.ShowFiles, update.ShowAssets, update.ShowWorkPhoneNumbers, update.ShowOrgChart, update.ShowCustomFields, update.ShowCalendarMenu, update.ShowAllLeavesInCalendar)),
            ErrorCodes.Forbidden,
            ErrorType.Forbidden);
    }

    private async Task SetAccessAsync(Guid tenantId, Guid ownerId, bool showOrgChart, bool showBirthdays, bool showPhones)
    {
        ActAs(tenantId, ownerId);
        var result = await ExecuteCommandAsync(new UpdateAccessSettingsCommand(
            ShowOrgInfo: true, ShowUpcomingLeaves: true, ShowUpcomingBirthdays: showBirthdays, ShowEventCalendar: true, ShowFiles: true, ShowAssets: true,
            ShowWorkPhoneNumbers: showPhones, ShowOrgChart: showOrgChart, ShowCustomFields: false, ShowCalendarMenu: true, ShowAllLeavesInCalendar: false));
        result.IsSuccess.ShouldBeTrue();
    }
}
