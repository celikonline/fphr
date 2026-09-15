using Sense.Modules.Employee.Domain.Employees;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Career;

/// <summary>One level of a career record's org placement (web/src/services/employees.service.ts).</summary>
public sealed record OrgAssignmentDto(Guid OrgUnitTypeId, Guid OrgUnitId);

/// <summary>
/// A career period as shown on the card's "Kariyer" tab. ChangeType/EmploymentType are C# enums, so they serialize as
/// camelCase strings ("promotion", "fullTime") exactly like the employee card's own enum fields.
/// </summary>
public sealed record CareerRecordDto(
    Guid Id,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    CareerChangeType ChangeType,
    EmploymentType? EmploymentType,
    Guid? ManagerEmployeeId,
    string? ManagerDisplayName,
    string? Note,
    IReadOnlyList<OrgAssignmentDto> Assignments,
    bool IsCurrent);

/// <summary>
/// Reads an employee's career timeline (newest first). An employee that predates career history (or was created by a
/// seed/quick-add path) has no rows yet: the current values are shown as an implicit, still-open Hire record whose id
/// is the employee id - the same id it gets when the first write materializes it, so editing it just works.
/// </summary>
public sealed class CareerReader(IEmployeeReadStore store, CareerService career)
{
    public async Task<IReadOnlyList<CareerRecordDto>> ReadAsync(EmployeeEntity employee, CancellationToken ct)
    {
        var today = career.Today;
        var records = employee.CareerTimeline;

        var managerIds = records.Count == 0
            ? (employee.ManagerEmployeeId is { } own ? [own] : new List<Guid>())
            : records.Where(r => r.ManagerEmployeeId is not null).Select(r => r.ManagerEmployeeId!.Value).Distinct().ToList();
        var names = await store.GetDisplayNamesAsync(managerIds, ct).ConfigureAwait(false);

        if (records.Count == 0)
        {
            if (!employee.HasCareerData)
            {
                return [];
            }

            var placement = await career.LoadPlacementAsync(ct).ConfigureAwait(false);
            return
            [
                new CareerRecordDto(
                    employee.Id,
                    employee.ImplicitCareerStart(today),
                    null,
                    CareerChangeType.Hire,
                    employee.EmploymentType,
                    employee.ManagerEmployeeId,
                    ManagerName(names, employee.ManagerEmployeeId),
                    null,
                    placement.ChainFor(employee.OrgUnitId).Select(a => new OrgAssignmentDto(a.OrgUnitTypeId, a.OrgUnitId)).ToList(),
                    IsCurrent: true),
            ];
        }

        var current = employee.CurrentCareerRecord(today);
        return records
            .OrderByDescending(r => r.ValidFrom)
            .ThenByDescending(r => r.Id)
            .Select(r => new CareerRecordDto(
                r.Id,
                r.ValidFrom,
                r.ValidTo,
                r.ChangeType,
                r.EmploymentType,
                r.ManagerEmployeeId,
                ManagerName(names, r.ManagerEmployeeId),
                r.Note,
                r.Assignments.Select(a => new OrgAssignmentDto(a.OrgUnitTypeId, a.OrgUnitId)).ToList(),
                r.Id == current?.Id))
            .ToList();
    }

    private static string? ManagerName(IReadOnlyDictionary<Guid, string> names, Guid? managerId) =>
        managerId is { } id ? names.GetValueOrDefault(id) : null;
}
