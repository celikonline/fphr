using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Infrastructure.Persistence;
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
}
