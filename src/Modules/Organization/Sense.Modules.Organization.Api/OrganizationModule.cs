using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Organization.Api;

/// <summary>
/// Organization module composition root: dynamic org hierarchy (OrgUnitType/OrgUnit), Company
/// settings, and the OrgUnitApprovalPolicyAssignment stub (roadmap Faz 0 item #4).
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
        services.AddModuleDbContext<OrganizationDbContext>(configuration, OrganizationDbContext.SchemaName);

        // Register MediatR-style command/query handlers for this module
        services.AddModuleHandlers(OrganizationDbContext.SchemaName, typeof(Sense.Modules.Organization.Application.OrganizationErrors).Assembly, typeof(OrganizationDbContext).Assembly);

        // Register repositories
        services.AddScoped<IOrgUnitTypeRepository, OrgUnitTypeRepository>();
        services.AddScoped<IOrgUnitRepository, OrgUnitRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IOrgUnitApprovalPolicyAssignmentRepository, OrgUnitApprovalPolicyAssignmentRepository>();
        services.AddScoped<IHolidayCalendarRepository, HolidayCalendarRepository>();
        services.AddScoped<IHolidayCalendarEntryRepository, HolidayCalendarEntryRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();

        // Cross-module read contracts consumed by other modules (see IOrgUnitLookup / ILocationLookup)
        services.AddScoped<IOrgUnitLookup, OrgUnitLookup>();

        // Cross-module read contract for a tenant's effective holiday calendar (roadmap Faz 0 item #5)
        services.AddScoped<IHolidayCalendarLookup, HolidayCalendarLookup>();
        services.AddScoped<ILocationLookup, LocationLookup>();
    }
}
