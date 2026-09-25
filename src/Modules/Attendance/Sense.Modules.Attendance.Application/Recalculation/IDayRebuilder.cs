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

/// <summary>
/// Re-derives an existing day's plan/anomalies from its stored check-in/out (no punches involved), through the same
/// <see cref="AttendanceDays.IPlannedDayResolver"/> + <see cref="AttendanceDays.AttendanceDayUpsert"/> path check-in channels use, so
/// the ShiftDefinition rule set (tolerances, overtime thresholds) is applied.
/// I0 CONNECTION POINT: the punch-based rebuild (B1 DayFromPunches) replaces this class behind <see cref="IDayRebuilder"/>
/// (DI line in AttendanceServiceCollectionExtensions.AddAttendanceServices) - derive check-in/out from
/// IPunchRepository.ListForDayAsync, then call AttendanceDayUpsert.UpsertAsync(days, planner, ...) the same way.
/// </summary>
public sealed class ExistingRecordDayRebuilder(
    IAttendanceDayRepository days, AttendanceDays.IPlannedDayResolver planner, IShiftSettingsRepository settings) : IDayRebuilder
{
    public async Task<AttendanceDay?> RebuildAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        var limits = AttendanceDays.AttendanceDayUpsert.ToShiftLimits(await settings.FindAsync(cancellationToken).ConfigureAwait(false));
        var overnight = existing.CheckIn is { } checkIn && existing.CheckOut is { } checkOut && checkOut <= checkIn;
        var result = await AttendanceDays.AttendanceDayUpsert.UpsertAsync(
            days, planner, tenantId, employeeId, date, existing.CheckIn, existing.CheckOut, existing.Source, overnight, cancellationToken,
            existing.ActualBreakMinutes, limits).ConfigureAwait(false);
        return result.IsFailure ? null : result.Value;
    }
}
