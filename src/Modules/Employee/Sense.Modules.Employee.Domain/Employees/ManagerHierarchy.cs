using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>Manager hierarchy rules shared by single and bulk manager assignment.</summary>
public static class ManagerHierarchy
{
    /// <summary>True if making <paramref name="managerId"/> the manager of <paramref name="employeeId"/> closes a loop (walks the new manager's chain upward).</summary>
    public static bool WouldCreateCycle(Guid employeeId, Guid managerId, IReadOnlyDictionary<Guid, Guid?> managerOf)
    {
        var visited = new HashSet<Guid>();
        Guid? current = managerId;
        while (current is { } node)
        {
            if (node == employeeId)
            {
                return true;
            }

            // Guards against a pre-existing loop elsewhere in the data.
            if (!visited.Add(node))
            {
                return false;
            }

            current = managerOf.TryGetValue(node, out var next) ? next : null;
        }

        return false;
    }

    /// <summary>All direct and indirect reports of <paramref name="managerId"/> (excluding the manager).</summary>
    public static IReadOnlySet<Guid> ReportsOf(Guid managerId, IReadOnlyDictionary<Guid, Guid?> managerOf)
    {
        var reportsByManager = managerOf.Where(kv => kv.Value is not null).ToLookup(kv => kv.Value!.Value, kv => kv.Key);
        var result = new HashSet<Guid>();
        var pending = new Queue<Guid>();
        pending.Enqueue(managerId);
        while (pending.TryDequeue(out var current))
        {
            foreach (var report in reportsByManager[current])
            {
                if (report != managerId && result.Add(report))
                {
                    pending.Enqueue(report);
                }
            }
        }

        return result;
    }
}

public sealed record EmployeeCreated(Guid EmployeeId, Guid TenantId, string DisplayName, Guid? UserId, Guid? OrgUnitId) : DomainEvent;

public sealed record EmployeeUpdated(Guid EmployeeId, Guid TenantId) : DomainEvent;

public sealed record EmployeeOrgUnitChanged(Guid EmployeeId, Guid TenantId, Guid? OrgUnitId) : DomainEvent;

public sealed record EmployeeManagerChanged(Guid EmployeeId, Guid TenantId, Guid? ManagerEmployeeId) : DomainEvent;

public sealed record EmployeeStatusChanged(Guid EmployeeId, Guid TenantId, EmployeeStatus Status) : DomainEvent;
