using FluentValidation;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>
/// İzinli günde çalışma detektörü (senseik-029, 11.3.6 çip kuralları): <see cref="AttendanceDay"/>'in kendi
/// Recompute'u yalnız günün planı/kaydına bakar, Leave verisini bilmez - bu, Leave.Contracts.IApprovedLeaveDateLookup
/// üzerinden AYRI bir geçiş olarak <see cref="AttendanceAnomaly.WorkedWhileOnLeave"/> bayrağını uygular. Bilinçli
/// olarak manuel kayıt/PDKS içe aktarma yoluna (RecordAttendanceHandler/AttendanceImportRowHandler) BAĞLANMAZ - bu,
/// Attendance'ın en sık çalışan yazma yollarına Leave.Infrastructure'a sert bir çalışma zamanı bağımlılığı ekler ve
/// yalnız Attendance'ı kuran mevcut test fixture'larını (Leave DI'ı olmadan) kırardı. Bunun yerine toplu yeniden
/// tarama (DetectWorkedWhileOnLeaveHandler) ve ay sonu kapatma (ApproveMonthlyTimesheetHandler) bu sınıfı çağırır -
/// her ikisi de Leave'in DI'da kayıtlı olduğu varsayılan (API) kapsamda çalışır.
/// </summary>
public sealed class LeaveOverlapChecker(IApprovedLeaveDateLookup leaveDates)
{
    /// <summary>Kayıt anı (tek gün): o gün için tek satırlık bir Leave sorgusu.</summary>
    public async Task ApplyAsync(Guid tenantId, AttendanceDay day, CancellationToken cancellationToken)
    {
        var overlap = await leaveDates.GetApprovedLeaveDatesAsync(tenantId, day.Date, day.Date, [day.EmployeeId], cancellationToken);
        day.ApplyLeaveOverlap(Overlaps(day, overlap));
    }

    /// <summary>Toplu tarama (import/ay sonu kapatma): birden çok gün ve çalışan için tek bir Leave sorgusuyla hepsini işaretler/temizler.</summary>
    public async Task<int> ApplyRangeAsync(Guid tenantId, IReadOnlyCollection<AttendanceDay> days, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (days.Count == 0)
        {
            return 0;
        }

        var employeeIds = days.Select(d => d.EmployeeId).Distinct().ToList();
        var leaveDaysResult = await leaveDates.GetApprovedLeaveDatesAsync(tenantId, from, to, employeeIds, cancellationToken);
        var byDay = leaveDaysResult.ToLookup(l => (l.EmployeeId, l.Date));

        var changed = 0;
        foreach (var day in days)
        {
            var before = day.Anomalies;
            day.ApplyLeaveOverlap(Overlaps(day, byDay[(day.EmployeeId, day.Date)]));
            if (day.Anomalies != before)
            {
                changed++;
            }
        }

        return changed;
    }

    private static bool Overlaps(AttendanceDay day, IEnumerable<ApprovedLeaveDay> leave)
    {
        var spans = leave.ToArray();
        if (spans.Any(l => l.DayFraction >= 1m)) return true;
        // Partial leave does not make the whole date non-working. Only evidence inside its time window conflicts.
        return spans.Any(l => l.StartsAt is { } start && l.EndsAt is { } end
            && ((day.CheckIn is { } checkIn && day.CheckOut is { } checkOut && checkIn < end && checkOut > start)
                || (day.CheckIn is { } onlyIn && day.CheckOut is null && onlyIn >= start && onlyIn < end)
                || (day.CheckOut is { } onlyOut && day.CheckIn is null && onlyOut > start && onlyOut <= end)));
    }
}

/// <summary>
/// Toplu yeniden tarama (senseik-029): retroaktif izin onayları (Leave'de onaylanan bir izin, Attendance'da zaten
/// kayıtlı bir günü sonradan "izinli günde çalışma" yapar) veya PDKS dışı düzeltmeler için elle/zamanlanmış olarak
/// tetiklenir. <paramref name="EmployeeIds"/> boş/null ise tenant'ın tüm aktif çalışanları taranır.
/// </summary>
[RequiresPermission("attendance.timesheet.edit")]
public sealed record DetectWorkedWhileOnLeaveCommand(DateOnly From, DateOnly To, IReadOnlyList<Guid>? EmployeeIds) : ICommand<int>;

public sealed class DetectWorkedWhileOnLeaveValidator : AbstractValidator<DetectWorkedWhileOnLeaveCommand>
{
    public DetectWorkedWhileOnLeaveValidator() =>
        RuleFor(x => x).Must(x => x.From <= x.To).WithMessage(AttendanceErrors.DateRangeInvalid);
}

public sealed class DetectWorkedWhileOnLeaveHandler(
    IAttendanceDayRepository days, IEmployeeDirectory employees, LeaveOverlapChecker checker, ITenantContext tenant)
    : ICommandHandler<DetectWorkedWhileOnLeaveCommand, int>
{
    public async Task<Result<int>> Handle(DetectWorkedWhileOnLeaveCommand command, CancellationToken cancellationToken)
    {
        var employeeIds = command.EmployeeIds is { Count: > 0 }
            ? command.EmployeeIds
            : await employees.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return 0;
        }

        var records = await days.ListForEmployeesAsync(employeeIds, command.From, command.To, cancellationToken);
        return await checker.ApplyRangeAsync(tenant.TenantId, records, command.From, command.To, cancellationToken);
    }
}
