using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;

namespace Sense.Modules.Attendance.Tests.Characterization;

/// <summary>
/// Zaman-devam programı (11.10) karakterizasyon verisi: okutma (Punch) tabanlı hesaplamaya geçmeden önce mevcut
/// <see cref="AttendanceDay.Record"/> + Payroll köprüsü okuyucularının çıktısını sabitleyen "karışık ay".
/// Plan 09:00-18:00 (480 dk net, 60 dk mola) varsayılan Pzt-Cum çizelgesiyle aynıdır.
/// </summary>
internal static class CharacterizationMonth
{
    public const int Year = 2026;
    public const int Month = 8;

    private static readonly TimeOnly PlanStart = new(9, 0);
    private static readonly TimeOnly PlanEnd = new(18, 0);
    private const int PlannedMinutes = 480;
    private const int PlannedBreakMinutes = 60;

    /// <summary>Karışık ay: tam gün, geç giriş, erken çıkış, fazla mesai, veri yok, pazar (plansız), gece vardiyası, PDKS gerçek molası, elle düzeltilmiş eksik gün.</summary>
    public static IReadOnlyList<AttendanceDay> MixedMonth(Guid tenantId, Guid employeeId)
    {
        var days = new List<AttendanceDay>
        {
            Planned(tenantId, employeeId, 3, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            Planned(tenantId, employeeId, 4, new TimeOnly(9, 30), new TimeOnly(18, 0)),
            Planned(tenantId, employeeId, 5, new TimeOnly(9, 0), new TimeOnly(17, 0)),
            Planned(tenantId, employeeId, 6, new TimeOnly(9, 0), new TimeOnly(20, 0)),
            Planned(tenantId, employeeId, 7, null, null),
            AttendanceDay.Record(
                tenantId, employeeId, Date(9), new TimeOnly(10, 0), new TimeOnly(14, 0),
                plannedStart: null, plannedEnd: null, plannedMinutes: 0, breakMinutes: 0, AttendanceDaySource.Manual),
            AttendanceDay.Record(
                tenantId, employeeId, Date(10), new TimeOnly(22, 0), new TimeOnly(6, 0),
                new TimeOnly(22, 0), new TimeOnly(6, 0), plannedMinutes: 420, breakMinutes: PlannedBreakMinutes, AttendanceDaySource.Import,
                overnight: true),
            AttendanceDay.Record(
                tenantId, employeeId, Date(11), new TimeOnly(8, 0), new TimeOnly(17, 30),
                PlanStart, PlanEnd, PlannedMinutes, PlannedBreakMinutes, AttendanceDaySource.Device, actualBreakMinutes: 45),
        };

        var corrected = Planned(tenantId, employeeId, 12, new TimeOnly(9, 0), new TimeOnly(13, 0));
        var resolved = corrected.ResolveWithOverride(480, "Saha ziyareti - elle düzeltildi");
        if (resolved.IsFailure)
        {
            throw new InvalidOperationException(resolved.Error.Code);
        }

        days.Add(corrected);
        return days;
    }

    public static DateOnly Date(int day) => new(Year, Month, day);

    private static AttendanceDay Planned(Guid tenantId, Guid employeeId, int day, TimeOnly? checkIn, TimeOnly? checkOut) =>
        AttendanceDay.Record(tenantId, employeeId, Date(day), checkIn, checkOut, PlanStart, PlanEnd, PlannedMinutes, PlannedBreakMinutes, AttendanceDaySource.Manual);
}

/// <summary>Gerçek PostgreSQL (Testcontainers) üzerinde Attendance modülü; <see cref="AttendanceIntegrationTests"/> ile aynı kurulum.</summary>
public abstract class AttendanceCharacterizationFixture : TestFixture
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

    /// <summary>Varsayılan Pzt-Cum 09:00-18:00 çizelgesini açar ve karışık ayı kaydeder.</summary>
    protected async Task SeedMixedMonthAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken)
    {
        using var write = ServiceProvider!.CreateScope();
        var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        await write.ServiceProvider.GetRequiredService<Application.WorkSchedules.WorkScheduleAssignmentService>()
            .EnsureDefaultScheduleAsync(tenantId, cancellationToken);
        db.AttendanceDays.AddRange(CharacterizationMonth.MixedMonth(tenantId, employeeId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
