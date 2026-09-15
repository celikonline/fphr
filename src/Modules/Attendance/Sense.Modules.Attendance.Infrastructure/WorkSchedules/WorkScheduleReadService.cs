using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using WorkScheduleEntity = Sense.Modules.Attendance.Domain.WorkSchedules.WorkSchedule;

namespace Sense.Modules.Attendance.Infrastructure.WorkSchedules;

/// <summary>Cross-module read port (roadmap #23) other modules (Leave) consume instead of guessing working days.</summary>
public sealed class WorkScheduleReadService(IEmployeeWorkScheduleAssignmentRepository assignments, IWorkScheduleRepository schedules)
    : IWorkScheduleReadService
{
    public async Task<EmployeeScheduleDay> GetForDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var assignment = await assignments.FindEffectiveOnAsync(employeeId, date, cancellationToken);
        var schedule = assignment is null
            ? await schedules.FindDefaultAsync(cancellationToken)
            : (await schedules.GetByIdAsync(assignment.WorkScheduleId, cancellationToken)) is { IsSuccess: true } found ? found.Value : null;

        if (schedule is null)
        {
            return new EmployeeScheduleDay(false, null, null, null, null, false);
        }

        var day = schedule.GetDay(date.DayOfWeek);
        return new EmployeeScheduleDay(day.IsWorkingDay, day.Start, day.End, day.BreakStart, day.BreakEnd, schedule.IncludeSaturdayWhenFullWeek);
    }

    public async Task<IReadOnlyList<DateOnly>> GetWorkingDaysAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var covering = (await assignments.ListCoveringRangeAsync(employeeId, from, to, cancellationToken)).OrderBy(a => a.EffectiveFrom).ToList();
        var scheduleCache = new Dictionary<Guid, WorkScheduleEntity>();
        WorkScheduleEntity? defaultSchedule = null;
        var defaultResolved = false;

        var days = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var scheduleId = covering.LastOrDefault(a => a.EffectiveFrom <= date)?.WorkScheduleId;
            WorkScheduleEntity? schedule;
            if (scheduleId is null)
            {
                if (!defaultResolved)
                {
                    defaultSchedule = await schedules.FindDefaultAsync(cancellationToken);
                    defaultResolved = true;
                }

                schedule = defaultSchedule;
            }
            else if (!scheduleCache.TryGetValue(scheduleId.Value, out schedule))
            {
                var found = await schedules.GetByIdAsync(scheduleId.Value, cancellationToken);
                schedule = found.IsSuccess ? found.Value : null;
                if (schedule is not null)
                {
                    scheduleCache[scheduleId.Value] = schedule;
                }
            }

            if (schedule is not null && schedule.GetDay(date.DayOfWeek).IsWorkingDay)
            {
                days.Add(date);
            }
        }

        return days;
    }

    public async Task<bool> IsWorkingDayAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        (await GetForDateAsync(employeeId, date, cancellationToken)).IsWorkingDay;
}
