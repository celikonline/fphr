using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>Implements Employee.Contracts.IEmployeeDirectory (selection resolution, contacts, unit membership).</summary>
public sealed class EmployeeDirectory(EmployeeDbContext context, EmployeeSelectionResolver resolver) : IEmployeeDirectory
{
    public Task<IReadOnlyList<Guid>> ResolveSelectionAsync(Guid tenantId, EmployeeSelection selection, CancellationToken ct = default) =>
        resolver.ResolveAsync(tenantId, selection, EmployeePermissions.EmployeesReadKey, ct);

    public Task<IReadOnlyList<EmployeeContact>> GetContactsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default) =>
        new EmployeeContactLookup(context).GetContactsAsync(tenantId, employeeIds, ct);

    public async Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsInOrgUnitsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken ct = default)
    {
        if (orgUnitIds.Count == 0)
        {
            return [];
        }

        var units = orgUnitIds.ToList();
        return await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OrgUnitId != null && units.Contains(e.OrgUnitId.Value) && e.Status != EmployeeStatus.Terminated)
            .Select(e => e.Id)
            .ToListAsync(ct);
    }
}

/// <summary>Implements Employee.Contracts.IEmployeeContactLookup (EmployeeDbContext only).</summary>
public sealed class EmployeeContactLookup(EmployeeDbContext context) : IEmployeeContactLookup
{
    public async Task<IReadOnlyList<EmployeeContact>> GetContactsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var ids = employeeIds.ToList();
        var rows = await context.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.Id))
            .ToListAsync(ct);

        return rows
            .Select(e => new EmployeeContact(e.Id, e.FirstName, e.LastName, e.WorkEmail, e.PersonalEmail, e.UserId, e.Status.ToString()))
            .ToList();
    }
}

/// <summary>Implements Employee.Contracts.IEmployeeUserLinker; saves immediately so callers in another module's unit of work need no Employee UoW.</summary>
public sealed class EmployeeUserLinker(EmployeeDbContext context) : IEmployeeUserLinker
{
    public async Task LinkUserAsync(Guid tenantId, Guid employeeId, Guid userId, CancellationToken ct = default)
    {
        var employee = await context.Employees.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct)
            ?? throw new InvalidOperationException(EmployeeLinkerMessages.EmployeeNotFound);

        if (employee.UserId == userId)
        {
            return;
        }

        employee.LinkUser(userId);
        await context.SaveChangesAsync(ct);
    }
}

internal static class EmployeeLinkerMessages
{
    public const string EmployeeNotFound = "Employee to link was not found in the tenant.";
}

/// <summary>Implements Employee.Contracts.IEmployeeProfileLookup (EmployeeDbContext only).</summary>
public sealed class EmployeeProfileLookup(EmployeeDbContext context) : IEmployeeProfileLookup
{
    public async Task<IReadOnlyList<EmployeeProfileDetail>> GetProfileDetailsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var ids = employeeIds.ToList();
        return await context.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.Id))
            .Select(e => new EmployeeProfileDetail(e.Id, e.HireDate, e.WorkPhone))
            .ToListAsync(ct);
    }
}
