using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Leave.Application;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Employees;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Leave.Infrastructure;

public static class LeaveServiceCollectionExtensions
{
    /// <summary>Everything the API module needs (DbContext and handler scanning are registered by the caller).</summary>
    public static IServiceCollection AddLeaveServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLeaveCoreServices(configuration);
        services.AddScoped<IWorkScheduleReader, MondayToFridayWorkScheduleReader>();
        services.AddScoped<ILeaveDayCalculator, LeaveDayCalculator>();
        services.AddScoped<LeaveRequestRules>();
        return services;
    }

    /// <summary>
    /// Services Leave's integration event handlers need, without dependencies on other modules' Infrastructure
    /// (IHolidayCalendarLookup, IWorkflowService) - Sense.Worker registers only these so DI validation in Development passes.
    /// </summary>
    public static IServiceCollection AddLeaveCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ILeaveUnitOfWork>(sp => sp.GetRequiredService<LeaveDbContext>());
        services.AddScoped<ILeaveRuleSetRepository, LeaveRuleSetRepository>();
        services.AddScoped<IEmployeeLeaveProfileRepository, EmployeeLeaveProfileRepository>();
        services.AddScoped<ILeaveLedgerRepository, LeaveLedgerRepository>();
        services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
        services.AddScoped<ILegislationParameterReader, LegislationParameterReader>();
        services.AddScoped<LeaveBalanceService>();
        return services;
    }

    /// <summary>Leave's cross-module integration event handlers (EmployeeHired, ApprovalCompleted, ApprovalRejected), registered explicitly for Sense.Worker.</summary>
    public static IServiceCollection AddLeaveIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, LeaveApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, LeaveApprovalRejectedHandler>();
        return services;
    }
}

/// <summary>Placeholder schedule until Attendance's work schedule contract exists (roadmap #23).</summary>
public sealed class MondayToFridayWorkScheduleReader : IWorkScheduleReader
{
    public Task<Func<DateOnly, bool>> GetWorkingDayPredicateAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<Func<DateOnly, bool>>(LeaveDayCounter.MondayToFriday);
}
