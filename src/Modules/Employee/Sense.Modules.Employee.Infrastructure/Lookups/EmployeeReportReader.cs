using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>
/// Implements Employee.Contracts.IEmployeeReportReader for the Reporting catalog's providers (roadmap #70). Loads the
/// aggregate with its career records (auto-include, split query) so the current per-level placement can be projected;
/// the caller supplies the visible set and the expanded unit filter - no permission logic lives here.
/// </summary>
public sealed class EmployeeReportReader(EmployeeDbContext context, TimeProvider clock) : IEmployeeReportReader
{
    public async Task<IReadOnlyList<EmployeeReportRow>> ListAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid>? employeeIds,
        IReadOnlyCollection<Guid>? orgUnitIds,
        bool includeTerminated,
        CancellationToken cancellationToken = default)
    {
        if (employeeIds is { Count: 0 })
        {
            return [];
        }

        var query = context.Employees.AsNoTracking().AsSplitQuery().Where(e => e.TenantId == tenantId);

        if (employeeIds is not null)
        {
            var ids = employeeIds.ToList();
            query = query.Where(e => ids.Contains(e.Id));
        }

        if (orgUnitIds is { Count: > 0 })
        {
            var units = orgUnitIds.ToList();
            query = query.Where(e => e.OrgUnitId != null && units.Contains(e.OrgUnitId.Value));
        }

        if (!includeTerminated)
        {
            query = query.Where(e => e.Status != EmployeeStatus.Terminated);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var employees = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync(cancellationToken);

        return employees.Select(e => Map(e, today)).ToList();
    }

    private static EmployeeReportRow Map(Domain.Employees.Employee employee, DateOnly today)
    {
        var current = employee.CurrentCareerRecord(today);
        var assignments = current is null
            ? new Dictionary<Guid, Guid>()
            : current.Assignments.GroupBy(a => a.OrgUnitTypeId).ToDictionary(g => g.Key, g => g.First().OrgUnitId);

        return new EmployeeReportRow(
            employee.Id,
            employee.FirstName,
            employee.LastName,
            employee.NationalId,
            employee.HireDate,
            employee.TerminationDate,
            employee.Status.ToString(),
            employee.OrgUnitId,
            employee.ManagerEmployeeId,
            employee.WorkEmail,
            employee.MobilePhone,
            (employee.EmploymentType ?? current?.EmploymentType)?.ToString(),
            employee.ContractType?.ToString(),
            employee.Gender.ToString(),
            employee.BirthDate,
            assignments);
    }
}
