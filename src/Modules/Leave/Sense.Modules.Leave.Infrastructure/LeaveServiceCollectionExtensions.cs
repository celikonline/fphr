using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Employees;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Overtime;
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
        services.AddScoped<IWorkScheduleReader, WorkScheduleReaderAdapter>();
        services.AddScoped<ILeaveDayCalculator, LeaveDayCalculator>();
        services.AddScoped<LeaveRequestRules>();
        services.AddScoped<OvertimeRequestRules>();
        services.AddScoped<LeaveRequestDetailBuilder>();
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
        services.AddScoped<IOvertimeRequestRepository, OvertimeRequestRepository>();
        services.AddScoped<IOvertimeConsentRepository, OvertimeConsentRepository>();
        services.AddScoped<IOvertimeConversionRepository, OvertimeConversionRepository>();
        return services;
    }

    /// <summary>Daily RecalculateBalances job (Sense.Worker): <see cref="Jobs.LeaveBalanceRecalculationJob"/> + its hosted service.</summary>
    public static IServiceCollection AddLeaveBalanceRecalculationJob(this IServiceCollection services)
    {
        services.AddSingleton<Jobs.LeaveBalanceRecalculationJob>();
        services.AddHostedService<Jobs.LeaveBalanceRecalculationHostedService>();
        return services;
    }

    /// <summary>Leave's cross-module integration event handlers (EmployeeHired, ApprovalCompleted, ApprovalRejected), registered explicitly for Sense.Worker.</summary>
    public static IServiceCollection AddLeaveIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, LeaveApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, LeaveApprovalRejectedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, OvertimeApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, OvertimeApprovalRejectedHandler>();
        return services;
    }
}

/// <summary>
/// Adapts Attendance.Contracts.IWorkScheduleReadService (roadmap #23) to Leave's IWorkScheduleReader port; replaces
/// the former Monday–Friday placeholder now that Attendance owns work schedules. Relies on the ambient ITenantContext
/// (set up by the outbox/request pipeline before this runs) the same way the placeholder ignored its tenantId parameter.
/// </summary>
public sealed class WorkScheduleReaderAdapter(IWorkScheduleReadService schedules) : IWorkScheduleReader
{
    public async Task<Func<DateOnly, bool>> GetWorkingDayPredicateAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var workingDays = (await schedules.GetWorkingDaysAsync(employeeId, from, to, cancellationToken)).ToHashSet();
        return workingDays.Contains;
    }

    public async Task<IReadOnlyList<ScheduledInterval>> GetScheduledIntervalsAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var intervals = new List<ScheduledInterval>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var scheduleDay = await schedules.GetForDateAsync(employeeId, day, cancellationToken);
            if (scheduleDay.IsWorkingDay && scheduleDay.Start is not null && scheduleDay.End is not null)
            {
                intervals.Add(new ScheduledInterval(day.ToDateTime(scheduleDay.Start.Value), day.ToDateTime(scheduleDay.End.Value)));
            }
        }

        return intervals;
    }
}
