using NSubstitute;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Infrastructure.Adapters;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// Karakterizasyon (zaman-devam W0-a): Attendance'ın karışık ay özeti (Sense.Modules.Attendance.Tests
/// Characterization/MonthlySummaryCharacterizationTests'te gerçek PostgreSQL üzerinde gözlenen çıktı) Payroll'un
/// <see cref="IWorkedTimeReader"/> portuna nasıl yansıyor. Okutma tabanlı hesaplamaya geçişte Attendance özeti aynı
/// kaldığı sürece bordronun gördüğü değerler de aynı kalmalı; beklenen değerler mevcut kod koşturulup sabitlendi.
/// </summary>
public sealed class AttendanceWorkedTimeCharacterizationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Mixed_month_attendance_summary_maps_to_baseline_worked_time()
    {
        var employeeId = Guid.NewGuid();
        var summaries = Substitute.For<IAttendanceMonthlySummaryReader>();
        summaries.GetAsync(TenantId, 2026, 8, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == employeeId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttendanceMonthlySummary>>(
            [
                new AttendanceMonthlySummary(
                    employeeId, WorkedDays: 8m, WorkedHours: 60m, MissingDays: 13m, OvertimeHours: 6.5m, IsPeriodLocked: false,
                    PublicHolidayDaysWorked: 0, WeeklyRestDaysWorked: 1),
            ]));

        var adapter = new AttendanceWorkedTimeReaderAdapter(summaries);
        var result = await adapter.GetAsync(TenantId, employeeId, 2026, 8, TestContext.Current.CancellationToken);

        result.ShouldBe(new WorkedTimeSummary(WorkedDays: 8, MissingDays: 13, OvertimeHours: 6.5m, PublicHolidayDaysWorked: 0, WeeklyRestDaysWorked: 1));
    }
}
