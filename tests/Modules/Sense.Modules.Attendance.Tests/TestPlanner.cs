using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// K2 plan çözümleyicisi (zaman-devam I0: her gün kurma kanalı <see cref="IPlannedDayResolver"/>'dan geçer) - vardiya ataması olmayan
/// testlerde yalnız çalışma çizelgesinden çözer (atama/tanım depoları boş).
/// </summary>
internal static class TestPlanner
{
    public static IPlannedDayResolver From(IWorkScheduleReadService schedules) =>
        new PlannedDayResolver(schedules, Substitute.For<IShiftAssignmentRepository>(), Substitute.For<IShiftDefinitionRepository>());
}
