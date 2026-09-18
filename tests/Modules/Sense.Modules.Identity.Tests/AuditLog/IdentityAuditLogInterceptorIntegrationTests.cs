using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.AuditLog;

/// <summary>
/// TALIMAT (faz-01 BE tur 1, md.5): IdentityAuditLogInterceptor round-trips a real AuditLogEntry row through
/// PostgreSQL for an IAuditLogged aggregate's Added state. Un-skipped in tur 3 - the AddAuditLogEntries
/// migration (20260916142651) landed on main, so identity.audit_log_entries now exists after MigrateAsync()
/// (status/faz-01/MIGRATION-ISTEGI.md, ADR-0005/ADR-0011).
/// senseik-018 (E-01): the Source scenarios below cover every branch of IdentityAuditLogInterceptor's
/// ResolveDefaultSource fallback plus the one explicit override (Import) - see that method's doc comment.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class IdentityAuditLogInterceptorIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddScoped<IdentityAuditLogInterceptor>();
        services.AddModuleDbContext<IdentityDbContext>(
            configuration, IdentityDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<IdentityAuditLogInterceptor>()));
    }

    [Fact]
    public async Task AddingARole_WritesAnAuditLogEntry_InTheSameTransaction()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var role = Role.CreateCustom(tenantId, "role_audit_test", "Audit Test Role", description: null).Value;
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.ResourceId == role.Id.ToString());
        entry.TenantId.ShouldBe(tenantId);
        entry.Action.ShouldBe(AuditActions.Create);
        entry.Resource.ShouldBe(nameof(Role));
    }

    private async Task<AuditLogEntry> SaveOneRoleAndReadItsEntryAsync(Guid tenantId, IdentityDbContext db)
    {
        var role = Role.CreateCustom(tenantId, $"role_{Guid.NewGuid():N}", "Audit Source Test Role", description: null).Value;
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.ResourceId == role.Id.ToString());
    }

    [Fact]
    public async Task AuthenticatedUser_WithNoExplicitSourceScope_WritesWebSource()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        SetCurrentUser(tenantId, Guid.NewGuid(), "web-user@test.local", new HashSet<string>(StringComparer.Ordinal));

        var entry = await SaveOneRoleAndReadItsEntryAsync(tenantId, db);

        entry.Source.ShouldBe(AuditSources.Web);
        entry.Actor.ShouldBe("web-user");
    }

    [Fact]
    public async Task AuthenticatedUser_WithApiKeyClaim_WritesApiSource()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        SetCurrentUser(tenantId, Guid.NewGuid(), "integration@test.local", new HashSet<string>(StringComparer.Ordinal), apiKeyId: Guid.NewGuid());

        var entry = await SaveOneRoleAndReadItsEntryAsync(tenantId, db);

        entry.Source.ShouldBe(AuditSources.Api);
    }

    [Fact]
    public async Task SystemUser_WritesSystemSource_AndTheCapitalizedActor_NeverTheLowercaseFallback()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        SetSystemUser();

        var entry = await SaveOneRoleAndReadItsEntryAsync(tenantId, db);

        entry.Source.ShouldBe(AuditSources.System);
        // Regression (senseik-018 §2/DataSeeder fix): a deliberate SystemUser (e.g. CurrentUserAccessor.UseSystem(),
        // which DataSeeder now wraps every SaveChangesAsync in) writes the capitalized "System" - never the
        // lowercase "system" placeholder that only a missing/anonymous context falls back to.
        entry.Actor.ShouldBe(WellKnownRoles.System);
        entry.Actor.ShouldNotBe("system");
    }

    [Fact]
    public async Task ExplicitImportScope_OverridesTheActorBasedDefault_EvenForARealAuthenticatedUser()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        SetCurrentUser(tenantId, Guid.NewGuid(), "importer@test.local", new HashSet<string>(StringComparer.Ordinal));

        AuditLogEntry entry;
        using (AuditSource!.BeginScope(AuditSources.Import))
        {
            entry = await SaveOneRoleAndReadItsEntryAsync(tenantId, db);
        }

        // The actor is still the real person who ran the import (KVKK m.12 "kim yaptı"); only the source differs
        // from the same actor's un-scoped Web default, proving the explicit scope wins over ResolveDefaultSource.
        entry.Actor.ShouldBe("importer");
        entry.Source.ShouldBe(AuditSources.Import);
    }
}
