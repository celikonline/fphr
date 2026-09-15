using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Import;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Employee.Api;

/// <summary>Employee module composition root (Faz 0: employee record, manager hierarchy, directory/org chart; full personnel record is Faz 1).</summary>
public sealed class EmployeeModule : IModule
{
    public string Name => EmployeeDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(EmployeeModule).Assembly,
        typeof(EmployeePermissions).Assembly,
        typeof(EmployeeDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => EmployeePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);

        // Handlers/validators live in the Application assembly (EmployeePermissions is in Contracts, so it must not be the scan anchor).
        // Contracts assembly is also required: EmployeeHired/EmployeeSuspended/EmployeeReactivated live there, and
        // IIntegrationEventOutbox/ModuleUnitOfWorkResolver resolve an event's owning module by its assembly.
        services.AddModuleHandlers(EmployeeDbContext.SchemaName, typeof(EmployeeErrors).Assembly, typeof(EmployeeDbContext).Assembly, typeof(EmployeePermissions).Assembly);

        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IEmployeeReadStore, EmployeeReadStore>();
        services.AddScoped<EmployeeSelectionResolver>();
        services.AddScoped<EmployeeDetailBuilder>();
        services.AddScoped<EmployeeReferenceValidator>();
        services.AddScoped<EmployeeSensitiveAccess>();
        services.AddScoped<IEmployeeUnitOfWork, EmployeeUnitOfWork>();

        // Custom field definitions (tenant admin catalog) + employee values (E4)
        services.AddScoped<ICustomFieldDefinitionRepository, CustomFieldDefinitionRepository>();
        services.AddScoped<CustomFieldValueReader>();

        // Termination wizard (E5a): reason catalog (system-seeded per tenant) + the advisory job-security scope check.
        // ILegislationParameterReader is registered by the Leave module (ADR 0008 single-owner table); Employee only
        // consumes it here, no registration needed on this side.
        services.AddScoped<ITerminationReasonRepository, TerminationReasonRepository>();
        services.AddScoped<TerminationReasonProvisioningService>();
        services.AddScoped<JobSecurityEvaluator>();

        // Career history (E2): shared by the career endpoints, PUT /employees/{id}, bulk assign-manager and the import
        services.AddScoped<OrgPlacementLoader>();
        services.AddScoped<CareerService>();
        services.AddScoped<CareerReader>();

        // Salary history + bank accounts (E3): IBANs are stored as AES-GCM ciphertext via the shared IEncryptionProvider
        services.AddScoped<IIbanProtector, EncryptedIbanProtector>();
        services.AddScoped<PaymentsService>();
        services.AddScoped<BankAccountWriter>();
        services.AddScoped<SelfEmployeeResolver>();

        // Data import template "employee.master" (collected by the DataTransfer module)
        services.AddScoped<IImportTemplate, EmployeeMasterImportTemplate>();
        services.AddScoped<IImportRowHandler, EmployeeImportRowHandler>();

        // Cross-module contracts consumed by Organization/Identity
        services.AddScoped<IEmployeeLookup, EmployeeLookup>();
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<IEmployeeUserLinker, EmployeeUserLinker>();
        services.AddScoped<IEmployeeScopeLookup, EmployeeScopeLookup>();

        // Cross-module contract consumed by Notifications (recipient resolution, also in Sense.Worker)
        services.AddScoped<IEmployeeContactLookup, EmployeeContactLookup>();

        // Cross-module contract consumed by Documents (document requirement evaluation): derived booleans only, never raw sensitive fields
        services.AddScoped<IEmployeeDocumentEligibilityLookup, EmployeeDocumentEligibilityLookup>();

        // Cross-module contract consumed by Analytics (workforce KPIs)
        services.AddScoped<IEmployeeAnalyticsQueries, EmployeeAnalyticsQueries>();
    }
}
