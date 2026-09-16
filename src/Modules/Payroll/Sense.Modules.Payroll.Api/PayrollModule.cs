using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Infrastructure;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Payroll.Api;

/// <summary>
/// Payroll module composition root (Faz 3 P0-P2): calculation engine, payroll periods/payslips and the
/// EmployeePayrollProfile CRUD handlers - no API endpoints yet (screens are P8), so this registers
/// DbContext + repositories + permissions for other modules/tests to call the handlers directly.
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
        services.AddModuleDbContext<PayrollDbContext>(configuration, PayrollDbContext.SchemaName);
        services.AddModuleHandlers(
            PayrollDbContext.SchemaName,
            typeof(PayrollLegislationSnapshotResolver).Assembly,
            typeof(PayrollDbContext).Assembly,
            typeof(IPayrollPeriodRepository).Assembly,
            typeof(PayrollPermissions).Assembly);
        services.AddPayrollServices(configuration);
    }
}
