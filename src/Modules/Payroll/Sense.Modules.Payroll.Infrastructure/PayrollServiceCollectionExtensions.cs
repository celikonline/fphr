using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Infrastructure.Persistence;

namespace Sense.Modules.Payroll.Infrastructure;

public static class PayrollServiceCollectionExtensions
{
    /// <summary>
    /// Everything the API module needs (DbContext and handler scanning are registered by the caller).
    /// <see cref="Sense.Shared.Contracts.Legislation.ILegislationParameterReader"/> is registered by the Leave module
    /// (ADR 0008 owner) via AddLeaveServices/AddLeaveCoreServices, which every host wiring Payroll must also register.
    /// </summary>
    public static IServiceCollection AddPayrollServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPayrollUnitOfWork>(sp => sp.GetRequiredService<PayrollDbContext>());
        services.AddScoped<IPayrollPeriodRepository, PayrollPeriodRepository>();
        services.AddScoped<IPayslipRepository, PayslipRepository>();
        services.AddScoped<IPayslipCalculationTraceRepository, PayslipCalculationTraceRepository>();
        services.AddScoped<IEmployeePayrollProfileRepository, EmployeePayrollProfileRepository>();
        return services;
    }
}
