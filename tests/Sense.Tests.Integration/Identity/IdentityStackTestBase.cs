using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Api;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Messaging;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Real Identity + Organization + Employee modules against PostgreSQL, driven through the Dispatcher with the full pipeline
/// (validation, AuthorizationBehaviour, access settings, unit of work). Callers are real seeded users with real system roles.
/// </summary>
public abstract class IdentityStackTestBase : TestFixture
{
    protected RecordingIdentityMailer Mailer { get; } = new();

    protected MutableTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        IReadOnlyList<IModule> modules = [new IdentityModule(), new OrganizationModule(), new EmployeeModule()];
        services.AddSingleton(modules);
        foreach (var module in modules)
        {
            module.AddModule(services, configuration);
        }

        services.Configure<FrontendOptions>(o =>
        {
            o.BaseUrl = "https://app.senseik.test";
            o.InviteAcceptPath = "/invite/{token}";
            o.PasswordResetPath = "/reset-password/{token}";
        });
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IIdentityMailer>(Mailer);
        services.AddSingleton<INotificationService, NoopNotificationService>();
        services.AddSingleton(new RsaSecurityKey(RSA.Create(2048)));
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected T Get<T>() where T : notnull => ServiceProvider!.GetRequiredService<T>();

    protected async Task<Guid> SeedTenantAsync(string slug)
    {
        var db = Get<IdentityDbContext>();
        var tenant = Tenant.Create(slug, slug, Time.GetUtcNow().UtcDateTime, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        using (TenantContextSetter!.BeginScope(tenant.Id, tenant.Slug))
        {
            db.Tenants.Add(tenant);
            TenantRoleSeeder.SeedSystemRoles(tenant.Id, Get<IPermissionCatalog>(), Get<IRoleRepository>(), PlatformTenant.IsPlatform(slug));
            await db.SaveChangesAsync();
        }

        return tenant.Id;
    }

    protected async Task<Guid> SeedUserAsync(
        Guid tenantId, string email, string? roleCode, Guid? employeeId = null, bool active = true, bool platformStaff = false)
    {
        var db = Get<IdentityDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var user = User.Create(tenantId, email, email.Split('@')[0], "Test", "tr").Value;
            if (roleCode is not null)
            {
                var role = await db.Roles.SingleAsync(r => r.Code == roleCode);
                user.SetRoles([role.Id]);
            }

            if (active)
            {
                user.SetPassword(Get<IPasswordHasher>().Hash("Password-12345"));
            }

            if (employeeId is { } eid)
            {
                user.LinkEmployee(eid);
            }

            user.SetPlatformStaff(platformStaff);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        }
    }

    protected sealed record OrgTree(Guid Root, Guid Sales, Guid SalesEmea, Guid Finance);

    protected async Task<OrgTree> SeedOrgTreeAsync(Guid tenantId)
    {
        var db = Get<OrganizationDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(t => t.Value).ToList();
            db.OrgUnitTypes.AddRange(types);
            var companyType = types.Single(t => t.Code == OrgUnitTypeCodes.Company).Id;
            var departmentType = types.Single(t => t.Code == OrgUnitTypeCodes.Department).Id;

            var root = OrgUnit.Create(tenantId, companyType, null, "Acme").Value;
            var sales = OrgUnit.Create(tenantId, departmentType, root.Id, "Sales").Value;
            var emea = OrgUnit.Create(tenantId, departmentType, sales.Id, "Sales EMEA").Value;
            var finance = OrgUnit.Create(tenantId, departmentType, root.Id, "Finance").Value;
            db.OrgUnits.AddRange(root, sales, emea, finance);
            await db.SaveChangesAsync();
            return new OrgTree(root.Id, sales.Id, emea.Id, finance.Id);
        }
    }

    protected async Task<Guid> SeedEmployeeAsync(
        Guid tenantId, string firstName, string lastName, Guid? orgUnitId = null, string? workEmail = null, string? personalEmail = null,
        string? workPhone = null, Guid? userId = null, DateOnly? birthDate = null)
    {
        var db = Get<EmployeeDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var employee = EmployeeEntity.Create(
                tenantId, new EmployeeInfo(firstName, lastName, null, workEmail, personalEmail, workPhone, null, birthDate), userId, orgUnitId).Value;
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            return employee.Id;
        }
    }

    /// <summary>Acts as a regular tenant user (same claims the JWT middleware would produce).</summary>
    protected void ActAs(Guid tenantId, Guid userId, Guid? employeeId = null) =>
        ActAs(tenantId, new AuthenticatedUser(
            UserId: userId, EmployeeId: employeeId, Email: "user@test.local", DisplayName: "User",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));

    protected void ActAs(Guid tenantId, AuthenticatedUser user)
    {
        ((CurrentUserAccessor)CurrentUser!).Set(user);
        TenantContextSetter!.BeginScope(tenantId);
    }

    protected static void ShouldFailWith<T>(Result<T> result, string code, ErrorType type)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
    }

    protected static void ShouldFailWith(Result result, string code, ErrorType type)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
    }
}

public sealed class RecordingIdentityMailer : IIdentityMailer
{
    public List<(string To, string Url)> Invitations { get; } = [];

    public List<(string To, string Url)> PasswordResets { get; } = [];

    public List<string> MfaResetNotices { get; } = [];

    public Task SendInvitationAsync(string toEmail, string toName, string tenantName, string inviteUrl, DateTime expiresAtUtc, CancellationToken ct)
    {
        Invitations.Add((toEmail, inviteUrl));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string toEmail, string toName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct)
    {
        PasswordResets.Add((toEmail, resetUrl));
        return Task.CompletedTask;
    }

    public Task SendWelcomeAsync(string toEmail, string toName, string tenantName, string loginUrl, CancellationToken ct) => Task.CompletedTask;

    public Task SendMfaResetNoticeAsync(string toEmail, string toName, string tenantName, CancellationToken ct)
    {
        MfaResetNotices.Add(toEmail);
        return Task.CompletedTask;
    }
}

public sealed class NoopNotificationService : INotificationService
{
    public Task SendToTenantAsync(Guid tenantId, NotificationDto notification) => Task.CompletedTask;

    public Task BroadcastJobProgressAsync(Guid tenantId, JobProgressDto progress) => Task.CompletedTask;

    public Task BroadcastAuditAlertAsync(Guid tenantId, AuditAlertDto alert) => Task.CompletedTask;
}

public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
