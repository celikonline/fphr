using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;

namespace Sense.Modules.Payroll.Application.Incentives;

/// <summary>An incentive assignment together with the definition it points at.</summary>
public sealed record IncentiveAssignmentInMonth(EmployeeIncentiveAssignment Assignment, IncentiveDefinition Definition);

/// <summary>
/// Every incentive assignment covering one month plus the definitions behind them, loaded once (two queries, however many
/// employees). The payroll run builds one plan per period and asks it per employee instead of querying per employee.
/// </summary>
public sealed class IncentiveMonthPlan
{
    private readonly ILookup<Guid, IncentiveAssignmentInMonth> _byEmployee;

    private IncentiveMonthPlan(DateOnly monthStart, IReadOnlyDictionary<Guid, IncentiveDefinition> definitions, ILookup<Guid, IncentiveAssignmentInMonth> byEmployee)
    {
        MonthStart = monthStart;
        Definitions = definitions;
        _byEmployee = byEmployee;
    }

    /// <summary>The first day of the month this plan describes.</summary>
    public DateOnly MonthStart { get; }

    /// <summary>The definitions referenced by the month's assignments, by id.</summary>
    public IReadOnlyDictionary<Guid, IncentiveDefinition> Definitions { get; }

    public static IncentiveMonthPlan Empty(DateOnly monthStart) =>
        new(EmployeeIncentiveAssignment.FirstOfMonth(monthStart), new Dictionary<Guid, IncentiveDefinition>(), Enumerable.Empty<IncentiveAssignmentInMonth>().ToLookup(x => x.Assignment.EmployeeId));

    public static async Task<IncentiveMonthPlan> LoadAsync(
        DateOnly monthStart,
        IEmployeeIncentiveAssignmentRepository assignments,
        IIncentiveDefinitionRepository definitions,
        CancellationToken cancellationToken = default)
    {
        var month = EmployeeIncentiveAssignment.FirstOfMonth(monthStart);
        var covering = await assignments.ListCoveringMonthAsync(month, cancellationToken);
        if (covering.Count == 0)
        {
            return Empty(month);
        }

        var byId = (await definitions.ListByIdsAsync(covering.Select(a => a.IncentiveDefinitionId).Distinct().ToList(), cancellationToken))
            .ToDictionary(d => d.Id);
        var pairs = covering
            .Where(a => byId.ContainsKey(a.IncentiveDefinitionId))
            .Select(a => new IncentiveAssignmentInMonth(a, byId[a.IncentiveDefinitionId]));
        return new IncentiveMonthPlan(month, byId, pairs.ToLookup(p => p.Assignment.EmployeeId));
    }

    /// <summary>The employees that have at least one assignment covering the month.</summary>
    public IEnumerable<Guid> EmployeeIds => _byEmployee.Select(g => g.Key);

    /// <summary>Every assignment of the employee covering the month, oldest start first, ties by id (the definition may be inactive or out of its date range).</summary>
    public IReadOnlyList<IncentiveAssignmentInMonth> ForEmployee(Guid employeeId) =>
        _byEmployee[employeeId].OrderBy(p => p.Assignment.ValidFromMonth).ThenBy(p => p.Assignment.Id).ToList();

    /// <summary>The employee's assignments whose definition is usable in the month (active and inside its date range).</summary>
    public IReadOnlyList<IncentiveAssignmentInMonth> EffectiveForEmployee(Guid employeeId) =>
        ForEmployee(employeeId).Where(p => p.Definition.IsEffectiveInMonth(MonthStart)).ToList();

    /// <summary>The employee's effective assignment of one category: the latest-starting one when (in a race) several overlap.</summary>
    public IncentiveAssignmentInMonth? EffectiveOfCategory(Guid employeeId, IncentiveCategory category) =>
        EffectiveForEmployee(employeeId).LastOrDefault(p => p.Definition.Category == category);
}
