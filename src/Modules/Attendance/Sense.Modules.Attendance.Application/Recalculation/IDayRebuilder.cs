using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Application.Recalculation;

/// <summary>
/// Port: rebuilds ONE (employee, date) <see cref="AttendanceDay"/> from its source facts (punches + resolved plan) and
/// returns the day, or null when there is nothing to build (no record and no punches) or the month is locked. The recalculator
/// owns the K6 rules (approved months skipped, resolution/override preserved, chunking, idempotence); the rebuilder owns only the
/// derivation. It does not save - the recalculator commits per chunk.
/// </summary>
public interface IDayRebuilder
{
    Task<AttendanceDay?> RebuildAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>
/// Punch-based rebuild (zaman-devam I0: B1 okutma hattı + B2 K2 planı + B3 yeniden hesap): the day is re-derived from its punches
/// through the same <see cref="PunchDayWriter"/> every check-in channel uses, so the plan comes from <see cref="IPlannedDayResolver"/>
/// (vardiya ataması + tanım kuralları → çalışma çizelgesi → plansız) and the Incalculable rules (tutarsız okutma, plansız okutma,
/// dinamik vardiya eşitliği, gece vardiyası çakışması) are the channels' own. Rolling deploy: a day an older API wrote without punches
/// is first converted to punches (<see cref="PunchDayWriter.LoadDayAsync"/> with <c>seedLegacy</c>), so its check-in/out survive.
/// Tenant shift limits apply to every rebuilt day. One writer per scope (the recalculator's chunk scope), so plan windows, month locks
/// and days created during the run are cached across calls.
/// </summary>
public sealed class PunchDayRebuilder(
    IAttendanceDayRepository days, IPunchRepository punches, IPlannedDayResolver planner, IMonthlyTimesheetApprovalRepository approvals,
    IShiftSettingsRepository settings, ITenantLocalClock clock) : IDayRebuilder
{
    private (Guid TenantId, PunchDayWriter Writer, ShiftLimits Limits)? _session;

    public async Task<AttendanceDay?> RebuildAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var (writer, limits) = await SessionAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (await writer.IsLockedAsync(employeeId, date, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var dayPunches = await writer.LoadDayAsync(employeeId, date, seedLegacy: true, cancellationToken).ConfigureAwait(false);
        var existing = await writer.FindDayAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (existing is null && dayPunches.Count == 0)
        {
            return null;
        }

        var rebuilt = await writer.RebuildAsync(employeeId, date, existing?.Source, limits, forceIncalculable: false, cancellationToken).ConfigureAwait(false);
        return rebuilt.IsFailure ? null : rebuilt.Value;
    }

    private async Task<(PunchDayWriter Writer, ShiftLimits Limits)> SessionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_session is { } session && session.TenantId == tenantId)
        {
            return (session.Writer, session.Limits);
        }

        var zone = await clock.GetTimeZoneAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var now = await clock.NowAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var limits = AttendanceDayUpsert.ToShiftLimits(await settings.FindAsync(cancellationToken).ConfigureAwait(false));
        var writer = new PunchDayWriter(tenantId, days, punches, planner, approvals, zone, now.Utc);
        _session = (tenantId, writer, limits);
        return (writer, limits);
    }
}
