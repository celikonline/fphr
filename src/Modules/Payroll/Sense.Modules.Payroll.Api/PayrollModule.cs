using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Infrastructure;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Payroll.Api;

/// <summary>
/// Payroll module composition root (Faz 3 P0-P3): calculation engine, payroll periods/payslips, the
/// EmployeePayrollProfile CRUD handlers, EmployeePayrollProfilesController (P2), and (P3) PayrollPeriodsController
/// (open/run/recalculate-dirty/dirty-flags) - profile and period management are API-only, the web screens themselves
/// are still P8. RecalculateAllPayslipsHandler depends on Employee's IEmployeeDirectory/IEmployeeSalaryLookup, which
/// this host (Sense.Api) gets from EmployeeModule.AddModule being registered alongside this one (ModuleCatalog).
/// </summary>
public sealed class PayrollModule : IModule
{
    public string Name => PayrollDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(PayrollModule).Assembly,
        typeof(PayrollPermissions).Assembly,
        typeof(PayrollDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => PayrollPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Audit trail for IAuditLogged aggregates (payroll periods) - same wiring as LeaveModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<PayrollDbContext>(
            configuration, PayrollDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<PayrollDbContext>>();
        services.AddModuleHandlers(
            PayrollDbContext.SchemaName,
            typeof(PayrollLegislationSnapshotResolver).Assembly,
            typeof(PayrollDbContext).Assembly,
            typeof(IPayrollPeriodRepository).Assembly,
            typeof(PayrollPermissions).Assembly);
        services.AddPayrollServices(configuration);

        // The closing checklist reads the employee directory, which only the API host provides (Sense.Worker has no consumer for it).
        services.AddScoped<Sense.Modules.Payroll.Application.Closing.ClosingChecklistService>();
        services.AddScoped<Sense.Modules.Payroll.Application.Closing.IClosingChecklistGate>(
            sp => sp.GetRequiredService<Sense.Modules.Payroll.Application.Closing.ClosingChecklistService>());
    }
}
