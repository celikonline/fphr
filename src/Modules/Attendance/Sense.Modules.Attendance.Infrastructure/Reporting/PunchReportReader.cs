using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Okutma raporlarının denetim amaçlı okuyucusu: <see cref="Sense.Modules.Attendance.Domain.IPunchRepository"/> yumuşak silinmiş
/// okutmaları hiç döndürmez; "Elle Girilen Kayıtlar" raporu ise silinmiş elle kayıtları (gerekçe + silen kullanıcı) da göstermelidir.
/// Açık <c>tenantId</c> ile süzer (kiracı filtresi atlanır), yalnızca okur.
/// </summary>
public interface IPunchReportReader
{
    /// <summary>
    /// [from, to] WorkDate aralığındaki elle girilmiş okutmalar (<c>IsManual</c> ya da kaynak Manual/Request), silinmişler DAHİL;
    /// <paramref name="employeeIds"/> zorunlu kapsamdır (boş küme boş sonuç).
    /// </summary>
    Task<IReadOnlyList<Punch>> ListManualForRangeAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}

public sealed class PunchReportReader(AttendanceDbContext context) : IPunchReportReader
{
    public async Task<IReadOnlyList<Punch>> ListManualForRangeAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        return await context.Punches.IgnoreQueryFilters([ModuleDbContext.TenantFilter])
            .Where(p => p.TenantId == tenantId && p.WorkDate >= from && p.WorkDate <= to && employeeIds.Contains(p.EmployeeId)
                && (p.IsManual || p.Source == PunchSource.Manual || p.Source == PunchSource.Request))
            .OrderBy(p => p.EmployeeId).ThenBy(p => p.OccurredAtUtc)
            .ToListAsync(cancellationToken);
    }
}
