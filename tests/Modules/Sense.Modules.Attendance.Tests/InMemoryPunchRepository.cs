using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// Handler testleri için <see cref="IPunchRepository"/>: eklenen okutmalar anında görünür (kaydetme beklemeden) ve aynı
/// örnekler döner, böylece yumuşak silme gibi değişiklikler tekrar sorgulanınca da görülür. Silinmişler sorgulardan düşer.
/// </summary>
internal sealed class InMemoryPunchRepository : IPunchRepository
{
    private readonly List<Punch> _punches = [];

    /// <summary>Silinmişler dahil tüm okutmalar (test doğrulamaları için).</summary>
    public IReadOnlyList<Punch> All => _punches;

    public IReadOnlyList<Punch> Live => _punches.Where(p => !p.IsDeleted).OrderBy(p => p.OccurredAtUtc).ToList();

    public void Add(Punch punch) => _punches.Add(punch);

    public Task<IReadOnlyList<Punch>> ListForDayAsync(Guid tenantId, Guid employeeId, DateOnly workDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Punch>>(
            Live.Where(p => p.TenantId == tenantId && p.EmployeeId == employeeId && p.WorkDate == workDate).ToList());

    public Task<IReadOnlyList<Punch>> ListForRangeAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Punch>>(
            Live.Where(p => p.TenantId == tenantId && p.WorkDate >= from && p.WorkDate <= to && (employeeIds is null || employeeIds.Contains(p.EmployeeId)))
                .ToList());

    public Task<bool> ExistsAsync(
        Guid tenantId, Guid employeeId, DateTime occurredAtUtc, PunchDirection direction, PunchSource source, CancellationToken cancellationToken = default) =>
        Task.FromResult(Live.Any(p =>
            p.TenantId == tenantId && p.EmployeeId == employeeId && p.OccurredAtUtc == occurredAtUtc && p.Direction == direction && p.Source == source));

    public Task<Punch?> FindByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_punches.FirstOrDefault(p => p.TenantId == tenantId && p.Id == id));

    public bool IsDuplicateKeyViolation(Exception exception) => exception is DuplicatePunchException;

    public void Discard(Punch punch) => _punches.Remove(punch);

    /// <summary>Kaydetme anında tekillik indeksi ihlalinin test eşi.</summary>
    public sealed class DuplicatePunchException() : Exception("ux_punches_tenant_employee_occurred_direction_source");
}
