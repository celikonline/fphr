using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>In-memory incentive definition repository; counts the bulk reads the month plan makes.</summary>
internal sealed class FakeIncentiveDefinitionRepository : IIncentiveDefinitionRepository
{
    public List<IncentiveDefinition> Items { get; } = [];

    public int ListByIdsCalls { get; private set; }

    public void Add(IncentiveDefinition definition) => Items.Add(definition);

    public Task<IncentiveDefinition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(d => d.Id == id));

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(d => d.Code == code));

    public Task<bool> TemplateExistsAsync(string templateKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(d => d.TemplateKey == templateKey));

    public Task<IReadOnlyList<IncentiveDefinition>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IncentiveDefinition>>(Items.Where(d => includeInactive || d.IsActive).OrderBy(d => d.Code, StringComparer.Ordinal).ToList());

    public Task<IReadOnlyList<IncentiveDefinition>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ListByIdsCalls++;
        return Task.FromResult<IReadOnlyList<IncentiveDefinition>>(Items.Where(d => ids.Contains(d.Id)).ToList());
    }
}

/// <summary>In-memory incentive assignment repository; counts the month reads.</summary>
internal sealed class FakeIncentiveAssignmentRepository : IEmployeeIncentiveAssignmentRepository
{
    public List<EmployeeIncentiveAssignment> Items { get; } = [];

    public int CoveringMonthCalls { get; private set; }

    public void Add(EmployeeIncentiveAssignment assignment) => Items.Add(assignment);

    public void Remove(EmployeeIncentiveAssignment assignment) => Items.Remove(assignment);

    public Task<EmployeeIncentiveAssignment?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmployeeIncentiveAssignment>>(Items.Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.ValidFromMonth).ToList());

    public Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListByEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmployeeIncentiveAssignment>>(Items.Where(a => employeeIds.Contains(a.EmployeeId)).ToList());

    public Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListCoveringMonthAsync(DateOnly monthStart, CancellationToken cancellationToken = default)
    {
        CoveringMonthCalls++;
        return Task.FromResult<IReadOnlyList<EmployeeIncentiveAssignment>>(Items.Where(a => a.IsCovered(monthStart)).ToList());
    }

    public Task<IReadOnlyList<EmployeeIncentiveAssignment>> ListByDefinitionAsync(Guid definitionId, DateOnly notEndedBefore, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmployeeIncentiveAssignment>>(
            Items.Where(a => a.IncentiveDefinitionId == definitionId && (a.ValidToMonth is null || a.ValidToMonth >= notEndedBefore)).ToList());
}
