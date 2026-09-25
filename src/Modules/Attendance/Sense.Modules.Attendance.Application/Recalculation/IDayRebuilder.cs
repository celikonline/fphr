using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Application.Recalculation;

/// <summary>
/// Port: rebuilds ONE (employee, date) <see cref="AttendanceDay"/> from its source facts (punches + resolved plan) and
/// returns the day, or null when there is nothing to build (no record and no punches). The recalculator owns the K6
/// rules (approved months skipped, resolution/override preserved, chunking, idempotence); the rebuilder owns only the
/// derivation. It does not save - the recalculator commits per chunk.
/// The default implementation (<see cref="ExistingRecordDayRebuilder"/>) re-resolves the plan for days that already have
/// a record; the punch-based implementation (B1 DayFromPunches + B2 PlannedDayResolver) replaces it in DI once merged.
/// </summary>
public interface IDayRebuilder
{
    Task<AttendanceDay?> RebuildAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>Re-derives an existing day's plan/anomalies from its stored check-in/out (no punches involved).</summary>
public sealed class ExistingRecordDayRebuilder(
    IAttendanceDayRepository days, Contracts.IWorkScheduleReadService schedules, IShiftSettingsRepository settings) : IDayRebuilder
{
    public async Task<AttendanceDay?> RebuildAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        var (start, end, minutes, breakMinutes) = await AttendanceDays.PlannedDayResolver.ResolveAsync(schedules, employeeId, date, cancellationToken).ConfigureAwait(false);
        var limits = AttendanceDays.AttendanceDayUpsert.ToShiftLimits(await settings.FindAsync(cancellationToken).ConfigureAwait(false));
        var overnight = existing.CheckIn is { } checkIn && existing.CheckOut is { } checkOut && checkOut <= checkIn;
        var result = existing.UpdateRecord(
            existing.CheckIn, existing.CheckOut, start, end, minutes, breakMinutes, existing.Source, overnight, existing.ActualBreakMinutes, limits);
        return result.IsFailure ? null : existing;
    }
}
