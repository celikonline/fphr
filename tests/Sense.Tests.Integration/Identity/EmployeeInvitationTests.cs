using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Invitations;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>Faz 0 item 10: employee invitations (single + bulk) create/link Employee-role users and pick the work e-mail first.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeInvitationTests : IdentityStackTestBase
{
    [Fact]
    public async Task InviteEmployee_PrefersWorkEmail_CreatesLinkedEmployeeUser_AndResendRenewsToken()
    {
        var tenantId = await SeedTenantAsync("invite-single");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        var both = await SeedEmployeeAsync(tenantId, "Ayşe", "Demir", workEmail: "Ayse.Demir@acme.test", personalEmail: "ayse@home.test");
        var personalOnly = await SeedEmployeeAsync(tenantId, "Bora", "Kaya", personalEmail: "bora@home.test");
        var noEmail = await SeedEmployeeAsync(tenantId, "Can", "Yok");

        ActAs(tenantId, ownerId);
        var sent = await ExecuteCommandAsync(new InviteEmployeeCommand(both));
        sent.IsSuccess.ShouldBeTrue(sent.IsFailure ? sent.Error.Code : null);
        sent.Value.ShouldBe(new EmployeeInvitationResultDto(both, EmployeeInvitationOutcomes.Sent, "ayse.demir@acme.test", "Work"));

        var user = await UserOfEmployeeAsync(tenantId, both);
        user.Email.ShouldBe("ayse.demir@acme.test");
        user.FirstName.ShouldBe("Ayşe");
        user.Status.ShouldBe(UserStatus.Invited);
        user.PreferredLanguage.ShouldBe("tr");
        var employeeRole = await RoleIdAsync(tenantId, SystemRoleCodes.Employee);
        user.Roles.Select(r => r.RoleId).ShouldBe([employeeRole]);
        (await EmployeeUserIdAsync(both)).ShouldBe(user.Id);

        var firstUrl = Mailer.Invitations.Last();
        firstUrl.To.ShouldBe("ayse.demir@acme.test");

        var personal = (await ExecuteCommandAsync(new InviteEmployeeCommand(personalOnly))).Value;
        personal.Outcome.ShouldBe(EmployeeInvitationOutcomes.Sent);
        personal.EmailSource.ShouldBe("Personal");
        personal.SentToEmail.ShouldBe("bora@home.test");

        ShouldFailWith(await ExecuteCommandAsync(new InviteEmployeeCommand(noEmail)), IdentityErrors.InvitationEmailRequired, ErrorType.Rule);
        ShouldFailWith(await ExecuteCommandAsync(new InviteEmployeeCommand(Guid.NewGuid())), IdentityErrors.InvitationEmployeeNotFound, ErrorType.NotFound);

        // Resend: same pending invitation, new token (the old link stops working).
        var resent = (await ExecuteCommandAsync(new InviteEmployeeCommand(both))).Value;
        resent.Outcome.ShouldBe(EmployeeInvitationOutcomes.Resent);
        var secondUrl = Mailer.Invitations.Last();
        secondUrl.Url.ShouldNotBe(firstUrl.Url);

        var invitations = await InvitationsOfAsync(tenantId, user.Id);
        var invitation = invitations.ShouldHaveSingleItem();
        invitation.SendCount.ShouldBe(2);
        invitation.EmailSource.ShouldBe(InvitationEmailSource.Work);
        invitation.InvitedByUserId.ShouldBe(ownerId);

        ShouldFailWith(await ExecuteQueryAsync(new GetInvitationPreviewQuery(TokenOf(firstUrl.Url))), IdentityErrors.InvitationNotFound, ErrorType.NotFound);
        (await ExecuteQueryAsync(new GetInvitationPreviewQuery(TokenOf(secondUrl.Url)))).Value.IsUsable.ShouldBeTrue();

        // Accepting activates the account; another invite is then reported as already active.
        (await ExecuteCommandAsync(new AcceptInvitationCommand(TokenOf(secondUrl.Url), "Gunes-Deniz-2026", true, IdentityDefaults.KnownPrivacyNoticeVersion, null, null))).IsSuccess.ShouldBeTrue();
        ActAs(tenantId, ownerId);
        (await ExecuteCommandAsync(new InviteEmployeeCommand(both))).Value.Outcome.ShouldBe(EmployeeInvitationOutcomes.AlreadyActive);
    }

    [Fact]
    public async Task InviteEmployee_LinksExistingUnlinkedUser_AndRejectsEmailOwnedByAnotherEmployeesUser()
    {
        var tenantId = await SeedTenantAsync("invite-link");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        var existingUserId = await SeedUserAsync(tenantId, "deniz@acme.test", SystemRoleCodes.HR, active: false);
        var deniz = await SeedEmployeeAsync(tenantId, "Deniz", "Var", workEmail: "deniz@acme.test");

        var otherEmployee = await SeedEmployeeAsync(tenantId, "Ela", "Bağlı");
        await SeedUserAsync(tenantId, "ela@acme.test", SystemRoleCodes.Employee, otherEmployee);
        var duplicate = await SeedEmployeeAsync(tenantId, "Ela", "Kopya", workEmail: "ela@acme.test");

        ActAs(tenantId, ownerId);
        var linked = (await ExecuteCommandAsync(new InviteEmployeeCommand(deniz))).Value;
        linked.Outcome.ShouldBe(EmployeeInvitationOutcomes.Sent);
        (await EmployeeUserIdAsync(deniz)).ShouldBe(existingUserId);
        (await UserOfEmployeeAsync(tenantId, deniz)).Id.ShouldBe(existingUserId);

        ShouldFailWith(await ExecuteCommandAsync(new InviteEmployeeCommand(duplicate)), IdentityErrors.InvitationEmailInUse, ErrorType.Conflict);
    }

    [Fact]
    public async Task BulkInvite_ReportsSentSkippedAndFailed_WithOneBatch()
    {
        var tenantId = await SeedTenantAsync("invite-bulk");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        var withWork = await SeedEmployeeAsync(tenantId, "İpek", "İş", workEmail: "ipek@acme.test");
        var withPersonal = await SeedEmployeeAsync(tenantId, "Jale", "Ev", personalEmail: "jale@home.test");
        var noEmail = await SeedEmployeeAsync(tenantId, "Kaan", "Yok");
        var activeRecord = await SeedEmployeeAsync(tenantId, "Leyla", "Aktif", workEmail: "leyla@acme.test");
        var activeUser = await SeedUserAsync(tenantId, "leyla@acme.test", SystemRoleCodes.Employee, activeRecord);
        await LinkEmployeeRecordAsync(tenantId, activeRecord, activeUser);
        var unknown = Guid.NewGuid();

        ActAs(tenantId, ownerId);
        var result = await ExecuteCommandAsync(new BulkInviteEmployeesCommand(
            new EmployeeSelection([withWork, withPersonal, noEmail, activeRecord, unknown], null, null)));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);

        var r = result.Value;
        (r.Requested, r.Succeeded, r.Skipped, r.Failed).ShouldBe((5, 2, 2, 1));
        r.Items.Single(i => i.EmployeeId == noEmail).ShouldBe(new BulkItemResult(noEmail, BulkItemOutcome.Skipped, IdentityErrors.InvitationEmailRequired));
        r.Items.Single(i => i.EmployeeId == activeRecord).ShouldBe(new BulkItemResult(activeRecord, BulkItemOutcome.Skipped, IdentityErrors.InvitationAlreadyActive));
        r.Items.Single(i => i.EmployeeId == unknown).ShouldBe(new BulkItemResult(unknown, BulkItemOutcome.Failed, IdentityErrors.InvitationEmployeeNotFound));

        var workUser = await UserOfEmployeeAsync(tenantId, withWork);
        var personalUser = await UserOfEmployeeAsync(tenantId, withPersonal);
        var batchIds = (await InvitationsOfAsync(tenantId, workUser.Id)).Concat(await InvitationsOfAsync(tenantId, personalUser.Id)).Select(i => i.BatchId).Distinct().ToList();
        batchIds.ShouldHaveSingleItem().ShouldNotBeNull();
        Mailer.Invitations.Select(i => i.To).ShouldBe(["ipek@acme.test", "jale@home.test"], ignoreOrder: true);

        // A caller without identity.users.invite is rejected outright.
        var plain = await SeedUserAsync(tenantId, "plain@acme.test", SystemRoleCodes.Employee);
        ActAs(tenantId, plain);
        ShouldFailWith(await ExecuteCommandAsync(new BulkInviteEmployeesCommand(new EmployeeSelection([withWork], null, null))), ErrorCodes.Forbidden, ErrorType.Forbidden);
    }

    private static string TokenOf(string url) => Uri.UnescapeDataString(url[(url.LastIndexOf('/') + 1)..]);

    private async Task<User> UserOfEmployeeAsync(Guid tenantId, Guid employeeId)
    {
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            return await Get<IdentityDbContext>().Users.AsNoTracking().SingleAsync(u => u.EmployeeId == employeeId, Ct);
        }
    }

    private async Task<Guid> RoleIdAsync(Guid tenantId, string code)
    {
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            return await Get<IdentityDbContext>().Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync(Ct);
        }
    }

    private async Task<List<Invitation>> InvitationsOfAsync(Guid tenantId, Guid userId)
    {
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            return await Get<IdentityDbContext>().Invitations.AsNoTracking().Where(i => i.UserId == userId).ToListAsync(Ct);
        }
    }

    private async Task<Guid?> EmployeeUserIdAsync(Guid employeeId) =>
        await Get<EmployeeDbContext>().Employees.AsNoTracking().IgnoreQueryFilters().Where(e => e.Id == employeeId).Select(e => e.UserId).SingleAsync(Ct);

    private async Task LinkEmployeeRecordAsync(Guid tenantId, Guid employeeId, Guid userId)
    {
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            await Get<IEmployeeUserLinker>().LinkUserAsync(tenantId, employeeId, userId, Ct);
        }
    }
}
