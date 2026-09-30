using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Invitations;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>E9: çalışan oluşturma formundaki "giriş bilgisi gönder" anahtarı (yalnız e-posta; SMS yok).</summary>
public sealed class InviteEmployeeSendCredentialsTests
{
    private sealed class Fixture
    {
        public IIdentityMailer Mailer { get; } = Substitute.For<IIdentityMailer>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public InviteEmployeeHandler Handler { get; }

        public Fixture(EmployeeContact contact)
        {
            var tenantEntity = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
            var tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(tenantEntity.Id);
            var employees = Substitute.For<IEmployeeDirectory>();
            employees.GetContactsAsync(tenantEntity.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns([contact]);
            var roles = Substitute.For<IRoleRepository>();
            roles.GetByCodeAsync(SystemRoleCodes.Employee, Arg.Any<CancellationToken>())
                .Returns(Role.CreateSystem(tenantEntity.Id, SystemRoleCodes.Employee));
            var tenants = Substitute.For<ITenantRepository>();
            tenants.GetByIdAsync(tenantEntity.Id, Arg.Any<CancellationToken>()).Returns(tenantEntity);
            var secrets = Substitute.For<ISecretGenerator>();
            secrets.NewToken().Returns("raw");
            secrets.Hash("raw").Returns("hash");
            var links = Substitute.For<IFrontendLinks>();
            links.InviteAccept("raw").Returns("https://app/invite?token=raw");
            var service = new EmployeeInvitationService(
                tenant, Substitute.For<ICurrentUser>(), Users, roles, tenants, Substitute.For<IInvitationRepository>(),
                Substitute.For<IEmployeeUserLinker>(), secrets, Mailer, links,
                Options.Create(new IdentityOptions()), TimeProvider.System);
            Handler = new InviteEmployeeHandler(tenant, employees, service);
        }
    }

    private static EmployeeContact Contact(string? work, string? personal) =>
        new(Guid.NewGuid(), "Ayşe", "Kaya", work, personal, null, "active");

    [Fact]
    public async Task Invitation_sent_when_flag_true()
    {
        var contact = Contact("ayse@acme.test", null);
        var f = new Fixture(contact);

        var result = await f.Handler.Handle(new InviteEmployeeCommand(contact.EmployeeId, SendCredentials: true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe(EmployeeInvitationOutcomes.Sent);
        await f.Mailer.Received(1).SendInvitationAsync("ayse@acme.test", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Default_flag_keeps_existing_behaviour()
    {
        var contact = Contact("ayse@acme.test", null);
        var f = new Fixture(contact);

        var result = await f.Handler.Handle(new InviteEmployeeCommand(contact.EmployeeId), CancellationToken.None);

        result.Value.Outcome.ShouldBe(EmployeeInvitationOutcomes.Sent);
    }

    [Fact]
    public async Task No_invitation_when_flag_false()
    {
        var contact = Contact("ayse@acme.test", null);
        var f = new Fixture(contact);

        var result = await f.Handler.Handle(new InviteEmployeeCommand(contact.EmployeeId, SendCredentials: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe(EmployeeInvitationOutcomes.NotRequested);
        f.Users.DidNotReceiveWithAnyArgs().Add(default!);
        await f.Mailer.DidNotReceiveWithAnyArgs().SendInvitationAsync(default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Missing_work_email_falls_back_to_personal_or_fails_clearly()
    {
        var personalOnly = Contact(null, "ayse.kisisel@mail.test");
        var f1 = new Fixture(personalOnly);
        var fallback = await f1.Handler.Handle(new InviteEmployeeCommand(personalOnly.EmployeeId), CancellationToken.None);
        fallback.IsSuccess.ShouldBeTrue();
        fallback.Value.SentToEmail.ShouldBe("ayse.kisisel@mail.test");

        var none = Contact(null, null);
        var f2 = new Fixture(none);
        var failed = await f2.Handler.Handle(new InviteEmployeeCommand(none.EmployeeId), CancellationToken.None);
        failed.IsFailure.ShouldBeTrue();
        failed.Error.Code.ShouldBe(IdentityErrors.InvitationEmailRequired);
        await f2.Mailer.DidNotReceiveWithAnyArgs().SendInvitationAsync(default!, default!, default!, default!, default, default);
    }
}
