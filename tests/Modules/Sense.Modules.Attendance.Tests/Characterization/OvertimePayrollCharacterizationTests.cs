using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Characterization;

/// <summary>
/// Karakterizasyon (zaman-devam W0-a): <see cref="IAttendanceOvertimePayrollReader"/>'ın karışık ay için Payroll'a
/// sunduğu fazla mesai dakikaları. Beklenen değerler mevcut kod koşturulup gözlenen çıktıdan sabitlendi.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class OvertimePayrollCharacterizationTests : AttendanceCharacterizationFixture
{
    // Gözlenen: AttendanceDay.OvertimeMinutes toplamı = 120 (6 Ağu) + 210 (9 Ağu pazar) + 30 (11 Ağu) = 360 dk.
    // (Aylık özetteki 390 dk'dan farklı: o, WorkedMinutes - PlannedMinutes üzerinden hesaplar.)
    private const int BaselineProvisionalOvertimeMinutes = 360;

    [Fact]
    public async Task Mixed_month_provisional_overtime_matches_baseline_when_equalization_disabled()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await SeedMixedMonthAsync(tenantId, employeeId, ct);

        using var read = ServiceProvider!.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAttendanceOvertimePayrollReader>();
        var line = (await reader.GetAsync(tenantId, CharacterizationMonth.Year, CharacterizationMonth.Month, [employeeId], ct)).ShouldHaveSingleItem();

        line.ShouldBe(new AttendanceOvertimePayrollLine(employeeId, BaselineProvisionalOvertimeMinutes, FinalizedOvertimeMinutes: null, EqualizationApplies: false));
    }

    [Fact]
    public async Task Mixed_month_without_closed_period_keeps_provisional_baseline_when_equalization_enabled()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await SeedMixedMonthAsync(tenantId, employeeId, ct);
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var settings = ShiftSettings.CreateDefault(tenantId);
            settings.UpdateEqualization(enabled: true, periodMonths: 2, anchorMonth: 1, hasWrittenAgreement: false).IsSuccess.ShouldBeTrue();
            db.ShiftSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IAttendanceOvertimePayrollReader>();
        var line = (await reader.GetAsync(tenantId, CharacterizationMonth.Year, CharacterizationMonth.Month, [employeeId], ct)).ShouldHaveSingleItem();

        line.ShouldBe(new AttendanceOvertimePayrollLine(employeeId, BaselineProvisionalOvertimeMinutes, FinalizedOvertimeMinutes: null, EqualizationApplies: true));
    }
}
