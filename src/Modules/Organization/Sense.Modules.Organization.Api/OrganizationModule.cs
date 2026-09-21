using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Infrastructure;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Organization.Api;

/// <summary>
/// Organization module composition root: dynamic org hierarchy (OrgUnitType/OrgUnit), Company
/// settings, holiday calendars, locations and per-unit approval policy assignments.
/// </summary>
public sealed class OrganizationModule : IModule
{
    public string Name => OrganizationDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(OrganizationModule).Assembly,
        typeof(OrganizationPermissions).Assembly,
        typeof(OrganizationDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => OrganizationPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Register DbContext for this module
        // Audit trail for IAuditLogged aggregates - same wiring as LeaveModule/ExpenseModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<OrganizationDbContext>(
            configuration, OrganizationDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<OrganizationDbContext>>();

        // Register MediatR-style command/query handlers for this module
        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor, and
        // Contracts events map to this module's outbox (IIntegrationEventOutbox).
        services.AddModuleHandlers(
            OrganizationDbContext.SchemaName,
            typeof(Sense.Modules.Organization.Application.OrganizationErrors).Assembly,
            typeof(OrganizationDbContext).Assembly,
            typeof(IOrgUnitRepository).Assembly,
            typeof(OrganizationPermissions).Assembly);

        // Register repositories
        services.AddScoped<IOrgUnitTypeRepository, OrgUnitTypeRepository>();
        services.AddScoped<IOrgUnitRepository, OrgUnitRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IOrgUnitApprovalPolicyAssignmentRepository, OrgUnitApprovalPolicyAssignmentRepository>();
        services.AddScoped<IHolidayCalendarRepository, HolidayCalendarRepository>();
        services.AddScoped<IHolidayCalendarEntryRepository, HolidayCalendarEntryRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<ICostCenterRepository, CostCenterRepository>();

        // Yeni kiracı Organization varsayılanları (wizard "hazırla" komutu); olay işleyicisi Worker'da kayıtlıdır.
        services.AddOrganizationTenantBootstrap();

        // Cross-module read contracts consumed by other modules (see IOrgUnitLookup / ILocationLookup)
        services.AddScoped<IOrgUnitLookup, OrgUnitLookup>();

        // Cross-module read contract for a tenant's effective holiday calendar (roadmap Faz 0 item #5)
        services.AddScoped<IHolidayCalendarLookup, HolidayCalendarLookup>();
        services.AddScoped<ILocationLookup, LocationLookup>();
        services.AddScoped<ICostCenterLookup, CostCenterLookup>();
        services.AddScoped<IEmployeeCostCenterReader, EmployeeCostCenterReader>();

        // Unit-based approval policy resolution for Workflow (11.3.02 "Birime onay süreci ataması")
        services.AddScoped<IOrgApprovalPolicyResolver, OrgApprovalPolicyResolver>();
    }
}
