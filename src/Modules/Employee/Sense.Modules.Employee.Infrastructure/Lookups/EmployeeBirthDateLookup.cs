using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>Implements Employee.Contracts.IEmployeeBirthDateLookup so Leave can apply the ≤18/≥50 statutory age
/// exception (4857 md.53/IV) without depending on Employee's Domain or Infrastructure.</summary>
public sealed class EmployeeBirthDateLookup(EmployeeDbContext context) : IEmployeeBirthDateLookup
{
    public async Task<DateOnly?> GetBirthDateAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        return await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => e.BirthDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> GetBirthDatesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return new Dictionary<Guid, DateOnly>();
        }

        var rows = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id) && e.BirthDate != null)
            .Select(e => new { e.Id, e.BirthDate })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.BirthDate!.Value);
    }
}
