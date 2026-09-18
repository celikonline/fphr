using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Infrastructure.Persistence;

namespace Sense.Modules.Attendance.Infrastructure.Lookups;

/// <summary>Implements Attendance.Contracts.IAttendanceOvertimePayrollReader (kart senseik-014 §6, Payroll bridge).</summary>
public sealed class AttendanceOvertimePayrollReader(AttendanceDbContext context) : IAttendanceOvertimePayrollReader
{
    public async Task<IReadOnlyList<AttendanceOvertimePayrollLine>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var ids = employeeIds.ToList();

        var equalizationEnabled = await context.ShiftSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (bool?)s.EqualizationEnabled)
            .FirstOrDefaultAsync(cancellationToken) ?? false;

        var provisionalByEmployee = await context.AttendanceDays.AsNoTracking()
            .Where(d => d.TenantId == tenantId && ids.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to)
            .GroupBy(d => d.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Minutes = g.Sum(d => d.OvertimeMinutes) })
            .ToDictionaryAsync(g => g.EmployeeId, g => g.Minutes, cancellationToken);

        if (provisionalByEmployee.Count == 0)
        {
            return [];
        }

        var finalizedByEmployee = equalizationEnabled
            ? await context.EqualizationPeriods.AsNoTracking()
                .Where(p => p.TenantId == tenantId && ids.Contains(p.EmployeeId) && p.Status == EqualizationPeriodStatus.Closed
                    && p.PeriodEnd >= from && p.PeriodEnd <= to)
                .GroupBy(p => p.EmployeeId)
                .Select(g => new { EmployeeId = g.Key, Minutes = g.Sum(p => p.TrueUpOvertimeMinutes) })
                .ToDictionaryAsync(g => g.EmployeeId, g => g.Minutes, cancellationToken)
            : new Dictionary<Guid, int>();

        return provisionalByEmployee
            .Select(kv => new AttendanceOvertimePayrollLine(
                kv.Key, kv.Value, finalizedByEmployee.TryGetValue(kv.Key, out var finalized) ? finalized : null, equalizationEnabled))
            .ToList();
    }
}
