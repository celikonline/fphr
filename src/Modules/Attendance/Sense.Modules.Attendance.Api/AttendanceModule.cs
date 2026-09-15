using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Import;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

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
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
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
    }
}
