using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Gerçek PostgreSQL (Testcontainers) üzerinde migrate edilmiş Attendance modülü; <see cref="AttendanceIntegrationTests"/> ile aynı kurulum.</summary>
public abstract class AttendancePostgresFixture : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceServices(configuration);
        services.AddModuleHandlers(
            AttendanceDbContext.SchemaName,
            typeof(AttendanceErrors).Assembly,
            typeof(AttendancePermissions).Assembly,
            typeof(AttendanceDbContext).Assembly,
            typeof(IWorkScheduleRepository).Assembly);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
}
