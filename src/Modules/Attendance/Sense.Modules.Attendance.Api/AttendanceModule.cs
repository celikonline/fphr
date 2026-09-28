using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Api.Terminals;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Import;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Jobs;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Attendance.Api;

/// <summary>Attendance module composition root: work schedules + employee assignments (11.3.6, package A1).</summary>
public sealed class AttendanceModule : IModule
{
    public string Name => AttendanceDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(AttendanceModule).Assembly,
        typeof(AttendancePermissions).Assembly,
        typeof(AttendanceDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => AttendancePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Audit trail for IAuditLogged aggregates (monthly timesheet approvals) - same wiring as LeaveModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<AttendanceDbContext>(
            configuration, AttendanceDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<AttendanceDbContext>>();
        services.AddModuleHandlers(
            AttendanceDbContext.SchemaName,
            typeof(AttendanceErrors).Assembly,
            typeof(AttendanceDbContext).Assembly,
            typeof(IWorkScheduleRepository).Assembly,
            typeof(AttendancePermissions).Assembly);
        services.AddAttendanceServices(configuration);

        // PDKS Excel import template "attendance.pdks" (collected by the DataTransfer module)
        services.AddScoped<IImportTemplate, AttendanceImportTemplate>();
        services.AddScoped<IImportRowHandler, AttendanceImportRowHandler>();

        // K7: vardiya dönemi otomatik kilit işi (JobType.ShiftPeriodAutoLock) - seed'lenmişti ama işleyicisi yoktu.
        services.AddScoped<IScheduledJobHandler, ShiftPeriodAutoLockJobHandler>();
        // Vardiya planı Excel şablonu "shift-plan" (Z3.2)
        services.AddScoped<IImportTemplate, ShiftPlanImportTemplate>();
        services.AddScoped<IImportRowHandler, ShiftPlanImportRowHandler>();
        // Z5.1 terminal okutma ucu: X-Terminal-Key kimlik doğrulama şeması + hız sınırı politikası (yalnız API).
        services.AddTerminalIngestEndpointSecurity(configuration);
        // Z5.4 toplu terminal kimliği Excel şablonu "terminal-credentials" (değer sütunu gizli; pepper yoksa şablon 503).
        services.AddScoped<IImportTemplate, Application.Terminals.TerminalCredentialImportTemplate>();
        services.AddScoped<IImportRowHandler, Application.Terminals.TerminalCredentialImportRowHandler>();
        services.AddScoped<Application.Terminals.ITerminalCredentialSaver, Infrastructure.Terminals.TerminalCredentialSaver>();
        services.AddOptions<TerminalCredentials.TerminalCredentialThrottleOptions>()
            .Bind(configuration.GetSection(TerminalCredentials.TerminalCredentialThrottleOptions.Section));
        services.AddSingleton<TerminalCredentials.TerminalCredentialThrottle>();
        services.AddOptions<Application.Terminals.TerminalCredentialImportOptions>()
            .Bind(configuration.GetSection(Application.Terminals.TerminalCredentialImportOptions.Section));
    }
}
