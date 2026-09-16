using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Compensatory;
using Sense.Modules.Attendance.Application.Employees;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Contracts.Events;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Attendance.Infrastructure.WorkSchedules;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Infrastructure;

public static class AttendanceServiceCollectionExtensions
{
    /// <summary>Everything the API module needs (DbContext and handler scanning are registered by the caller).</summary>
    public static IServiceCollection AddAttendanceServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAttendanceCoreServices(configuration);
        return services;
    }

    /// <summary>
    /// Services Attendance's integration event handlers need, without dependencies on other modules' Infrastructure -
    /// Sense.Worker registers only these so DI validation in Development passes.
    /// </summary>
    public static IServiceCollection AddAttendanceCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAttendanceUnitOfWork>(sp => sp.GetRequiredService<AttendanceDbContext>());
        services.AddScoped<IWorkScheduleRepository, WorkScheduleRepository>();
        services.AddScoped<IEmployeeWorkScheduleAssignmentRepository, EmployeeWorkScheduleAssignmentRepository>();
        services.AddScoped<IAttendanceDayRepository, AttendanceDayRepository>();
        services.AddScoped<IAnomalyResolutionRepository, AnomalyResolutionRepository>();
        services.AddScoped<IShiftSettingsRepository, ShiftSettingsRepository>();
        services.AddScoped<IShiftAssignmentRepository, ShiftAssignmentRepository>();
        services.AddScoped<IShiftPlanLockRepository, ShiftPlanLockRepository>();
        services.AddScoped<IEmployeeAvailabilityRepository, EmployeeAvailabilityRepository>();
        services.AddScoped<ICompensatoryWorkLedgerRepository, CompensatoryWorkLedgerRepository>();
        services.AddScoped<WorkScheduleAssignmentService>();
        services.AddScoped<IWorkScheduleReadService, WorkScheduleReadService>();
        return services;
    }

    /// <summary>Attendance's cross-module integration event handlers (EmployeeHired, TimesheetWeekCompleted), registered explicitly for Sense.Worker.</summary>
    public static IServiceCollection AddAttendanceIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredHandler>();
        services.AddScoped<IIntegrationEventHandler<TimesheetWeekCompleted>, TimesheetWeekCompletedHandler>();
        return services;
    }
}
