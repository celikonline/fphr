using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>Regresyon: sendInvitation:true, kullanıcı henüz kaydedilmediği için 404 user.not_found ile tüm işlemi geri alıyordu.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class CreateUserInvitationTests : IdentityStackTestBase
{
    private async Task<Guid> RoleIdAsync(Guid tenantId, string code)
    {
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            return (await Get<IdentityDbContext>().Roles.SingleAsync(r => r.Code == code, Ct)).Id;
        }
    }

    [Fact]
    public async Task CreateUser_WithSendInvitation_PersistsUser_AndSendsInvitationMail()
    {
        var tenantId = await SeedTenantAsync("create-user-invite");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        ActAs(tenantId, ownerId);

        var result = await ExecuteCommandAsync(new CreateUserCommand(
            "yeni@acme.test", "Yeni", "Kullanıcı", null, [await RoleIdAsync(tenantId, SystemRoleCodes.Employee)], null, true, null));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        Mailer.Invitations.ShouldContain(i => i.To == "yeni@acme.test");

        var db = Get<IdentityDbContext>();
        db.ChangeTracker.Clear();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var user = await db.Users.SingleAsync(u => u.Id == result.Value, Ct);
            user.Status.ShouldBe(UserStatus.Invited);
            (await db.Invitations.CountAsync(i => i.UserId == user.Id, Ct)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task CreateUser_WithoutInvitation_StillWorks()
    {
        var tenantId = await SeedTenantAsync("create-user-noinvite");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        ActAs(tenantId, ownerId);

        var result = await ExecuteCommandAsync(new CreateUserCommand(
            "sessiz@acme.test", "Sessiz", "Kullanıcı", null, [await RoleIdAsync(tenantId, SystemRoleCodes.Employee)], null, false, null));

        result.IsSuccess.ShouldBeTrue();
        Mailer.Invitations.ShouldBeEmpty();
    }
}
