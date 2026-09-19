using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Equalization;

/// <summary>
/// Denkleştirme dönemi durumu (m.63, kart senseik-014 §4). <see cref="ProvisionalOvertimeMinutes"/> is a running
/// estimate while the period is open (PROVİZYON, not final); once <see cref="IsClosed"/>, <see cref="TrueUpOvertimeMinutes"/>
/// is the legally final figure for that period. <see cref="Enabled"/> false means the tenant doesn't use m.63
/// denkleştirme - every other field is then meaningless (zeroed/null).
/// </summary>
public sealed record EqualizationStatusDto(
    bool Enabled, DateOnly? PeriodStart, DateOnly? PeriodEnd, int BudgetMinutes, int TotalWorkedMinutes, int ProvisionalOvertimeMinutes,
    bool IsClosed, int? TrueUpOvertimeMinutes);

/// <summary>
/// Bir çalışanın, verilen tarihi kapsayan denkleştirme dönemi durumu; dönem satırı yoksa açılır, açıksa bugüne kadarki
/// puantajdan yeniden hesaplanır (kalıcı ama her çağrıda tazelenen sayaç - artımlı biriktirme değil). Dönem bitmişse
/// (bugün &gt; PeriodEnd) aynı çağrıda kapatılır.
/// </summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record GetEqualizationStatusQuery(Guid EmployeeId, DateOnly Date) : IQuery<EqualizationStatusDto>;

public sealed class GetEqualizationStatusHandler(
    ITenantContext tenant, IShiftSettingsRepository shiftSettings, IEqualizationPeriodRepository periods, IAttendanceDayRepository days,
    IAttendanceUnitOfWork unitOfWork, ITenantLocalClock localClock, IDataScopeService dataScope)
    : IQueryHandler<GetEqualizationStatusQuery, EqualizationStatusDto>
{
    public async Task<Result<EqualizationStatusDto>> Handle(GetEqualizationStatusQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetView.Key, query.EmployeeId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var settings = await shiftSettings.FindAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null || !settings.EqualizationEnabled)
        {
            return new EqualizationStatusDto(false, null, null, 0, 0, 0, false, null);
        }

        var (periodStart, periodEnd) = EqualizationPeriodCalculator.GetPeriodBounds(query.Date, settings.EqualizationPeriodMonths, settings.EqualizationAnchorMonth);
        var period = await periods.FindCoveringAsync(query.EmployeeId, query.Date, cancellationToken).ConfigureAwait(false);
        if (period is null)
        {
            var weeks = EqualizationPeriodCalculator.CountWeeksByMonday(periodStart, periodEnd);
            period = EqualizationPeriod.Open(tenant.TenantId, query.EmployeeId, periodStart, periodEnd, weeks);
            periods.Add(period);
        }

        // "Bugün" kiracının yerel takvim günüdür (UTC değil): UTC+3 kiracıda gece yarısından sonraki dönem sonu bir gün geç görünmesin.
        var today = DateOnly.FromDateTime((await localClock.NowAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false)).Local);
        if (period.Status == EqualizationPeriodStatus.Open)
        {
            var sumTo = today < period.PeriodEnd ? today : period.PeriodEnd;
            var records = await days.ListForEmployeeAsync(query.EmployeeId, period.PeriodStart, sumTo, cancellationToken).ConfigureAwait(false);
            period.SetProvisionalTotal(records.Sum(d => d.NetWorkedMinutes));

            if (today > period.PeriodEnd)
            {
                period.Close(today);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new EqualizationStatusDto(
            true, period.PeriodStart, period.PeriodEnd, period.BudgetMinutes, period.TotalWorkedMinutes,
            Math.Max(period.TotalWorkedMinutes - period.BudgetMinutes, 0), period.Status == EqualizationPeriodStatus.Closed,
            period.Status == EqualizationPeriodStatus.Closed ? period.TrueUpOvertimeMinutes : null);
    }
}
