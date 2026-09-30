using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Characterization;

/// <summary>
/// Karakterizasyon (zaman-devam W0-a): okutma tabanlı yeniden hesaplamaya geçmeden önce <c>AttendanceDay.Record</c>'un
/// gün bazlı çıktısı ve <see cref="IAttendanceMonthlySummaryReader"/>'ın aylık özeti. Beklenen değerler bu test
/// yazılırken mevcut kod koşturulup gözlenen çıktıdan sabitlendi - davranış değişikliği bu testi kırmalıdır.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MonthlySummaryCharacterizationTests : AttendanceCharacterizationFixture
{
    // Gözlenen çıktı (mevcut kod, 2026-09-25). Not: 10 Ağustos gece vardiyasında ShiftSettings yok -> NightMinutes 0;
    // 9 Ağustos pazar (plansız) günü OvertimeMinutes'e tamamı (m.68 asgari molası düşülmüş net) yazılır.
    private static readonly string[] BaselineDays =
    [
        "03 W=480 O= N=480 B=60 Ni=0 OT=0 A=None R=Resolved",
        "04 W=450 O= N=450 B=60 Ni=0 OT=0 A=LateArrival, Underwork R=Unresolved",
        "05 W=420 O= N=420 B=60 Ni=0 OT=0 A=EarlyLeave, Underwork R=Unresolved",
        "06 W=600 O= N=600 B=60 Ni=0 OT=120 A=LateLeave, Overwork R=Unresolved",
        "07 W=0 O= N=0 B=0 Ni=0 OT=0 A=NoData R=Unresolved",
        "09 W=240 O= N=210 B=30 Ni=0 OT=210 A=UnplannedWork R=Unresolved",
        "10 W=420 O= N=420 B=60 Ni=0 OT=0 A=None R=Resolved",
        "11 W=510 O= N=510 B=60 Ni=0 OT=30 A=EarlyLeave, Overwork R=Unresolved",
        "12 W=180 O=480 N=210 B=30 Ni=0 OT=0 A=EarlyLeave, Underwork R=Resolved",
    ];

    [Fact]
    public async Task Mixed_month_days_match_baseline()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await SeedMixedMonthAsync(tenantId, employeeId, ct);

        using var read = ServiceProvider!.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var days = await db.AttendanceDays.AsNoTracking().Where(d => d.EmployeeId == employeeId).OrderBy(d => d.Date).ToListAsync(ct);

        var snapshot = days
            .Select(d =>
                $"{d.Date:dd} W={d.WorkedMinutes} O={d.OverrideWorkedMinutes} N={d.NetWorkedMinutes} B={d.BreakMinutesApplied} Ni={d.NightMinutes} OT={d.OvertimeMinutes} A={d.Anomalies} R={d.ResolutionState}")
            .ToArray();

        snapshot.ShouldBe(BaselineDays);
    }

    [Fact]
    public async Task Monthly_summary_matches_baseline_for_mixed_month()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await SeedMixedMonthAsync(tenantId, employeeId, ct);

        using var read = ServiceProvider!.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAttendanceMonthlySummaryReader>();
        var summary = (await reader.GetAsync(tenantId, CharacterizationMonth.Year, CharacterizationMonth.Month, [employeeId], ct)).ShouldHaveSingleItem();

        // Gözlenen: 8 çalışılan gün (pazar dahil), 3600 dk = 60 sa (12 Ağustos için override 480), Ağustos 2026'da 21 iş günü
        // - 8 = 13 eksik gün (pazar çalışması da eksik günden düşülüyor), fazla mesai 120 + 240 + 30 = 390 dk = 6,5 sa.
        summary.ShouldBe(new AttendanceMonthlySummary(
            employeeId, WorkedDays: 8m, WorkedHours: 60m, MissingDays: 13m, OvertimeHours: 6.5m, IsPeriodLocked: false,
            PublicHolidayDaysWorked: 0, WeeklyRestDaysWorked: 1));
    }
}
