using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain;

public interface ILeaveRuleSetRepository
{
    void Add(LeaveRuleSet ruleSet);

    Task<Result<LeaveRuleSet>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LeaveRuleSet?> FindDefaultAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaveRuleSet>> ListAsync(CancellationToken cancellationToken = default);

    Task<int> CountAssignedEmployeesAsync(Guid ruleSetId, CancellationToken cancellationToken = default);
}

public interface IEmployeeLeaveProfileRepository
{
    void Add(EmployeeLeaveProfile profile);

    Task<EmployeeLeaveProfile?> FindByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

public interface ILeaveLedgerRepository
{
    void AddRange(IEnumerable<LeaveLedgerEntry> entries);

    void RemoveRange(IEnumerable<LeaveLedgerEntry> entries);

    Task<IReadOnlyList<LeaveLedgerEntry>> ListAsync(Guid employeeId, Guid? leaveTypeId = null, CancellationToken cancellationToken = default);
}

public interface ILeaveRequestRepository
{
    void Add(LeaveRequest request);

    Task<LeaveRequest?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaveRequest>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Pending/approved requests of the employee intersecting [start, end].</summary>
    Task<IReadOnlyList<LeaveRequest>> ListActiveOverlappingAsync(Guid employeeId, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>Total days of the employee's pending requests of one type (reservations not yet in the ledger).</summary>
    Task<decimal> SumPendingDaysAsync(Guid employeeId, Guid leaveTypeId, CancellationToken cancellationToken = default);
}
