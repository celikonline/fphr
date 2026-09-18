using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain;

public interface ILeaveRuleSetRepository
{
    void Add(LeaveRuleSet ruleSet);

    void Remove(LeaveRuleSet ruleSet);

    Task<Result<LeaveRuleSet>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LeaveRuleSet?> FindDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>All rule sets (internal computation). API-facing reads use the bounded overload.</summary>
    Task<IReadOnlyList<LeaveRuleSet>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="take"/> rule sets (API-facing reads); the count is clamped to the shared page-size ceiling.</summary>
    Task<IReadOnlyList<LeaveRuleSet>> ListAsync(int take, CancellationToken cancellationToken = default);

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

    /// <summary>All of the employee's requests (internal computation). API-facing reads use the bounded overload.</summary>
    Task<IReadOnlyList<LeaveRequest>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="take"/> of the employee's requests (API-facing reads); the count is clamped to the shared page-size ceiling.</summary>
    Task<IReadOnlyList<LeaveRequest>> ListByEmployeeAsync(Guid employeeId, int take, CancellationToken cancellationToken = default);

    /// <summary>Pending/approved requests of the employee intersecting [start, end].</summary>
    Task<IReadOnlyList<LeaveRequest>> ListActiveOverlappingAsync(Guid employeeId, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>Total days of the employee's pending requests of one type (reservations not yet in the ledger).</summary>
    Task<decimal> SumPendingDaysAsync(Guid employeeId, Guid leaveTypeId, CancellationToken cancellationToken = default);

    /// <summary>Pending/approved requests intersecting [from, to]; <paramref name="employeeIds"/> null = every employee of the tenant.</summary>
    Task<IReadOnlyList<LeaveRequest>> ListActiveInRangeAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);
}

public interface IOvertimeRequestRepository
{
    void Add(OvertimeRequest request);

    Task<OvertimeRequest?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OvertimeRequest>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="take"/> of the employee's requests (API-facing reads); the count is clamped to the shared page-size ceiling.</summary>
    Task<IReadOnlyList<OvertimeRequest>> ListByEmployeeAsync(Guid employeeId, int take, CancellationToken cancellationToken = default);

    /// <summary>Pending/approved requests of the employee whose work date is inside [from, to].</summary>
    Task<IReadOnlyList<OvertimeRequest>> ListActiveAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public interface IOvertimeConsentRepository
{
    void Add(OvertimeConsent consent);

    Task<OvertimeConsent?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OvertimeConsent>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="take"/> of the employee's consents (API-facing reads); the count is clamped to the shared page-size ceiling.</summary>
    Task<IReadOnlyList<OvertimeConsent>> ListByEmployeeAsync(Guid employeeId, int take, CancellationToken cancellationToken = default);
}

public interface IOvertimeConversionRepository
{
    void Add(OvertimeConversion conversion);

    Task<IReadOnlyList<OvertimeConversion>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="take"/> of the employee's conversions (API-facing reads); the count is clamped to the shared page-size ceiling.</summary>
    Task<IReadOnlyList<OvertimeConversion>> ListByEmployeeAsync(Guid employeeId, int take, CancellationToken cancellationToken = default);
}

public interface IAbsenceRecordRepository
{
    void Add(AbsenceRecord record);

    Task<AbsenceRecord?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every row (originals and reversals) of the employee dated inside [from, to]; the caller decides what to show.</summary>
    Task<IReadOnlyList<AbsenceRecord>> ListAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Whether a reversal row already points at <paramref name="originalId"/> (an original can be reversed only once).</summary>
    Task<bool> ExistsReversalAsync(Guid originalId, CancellationToken cancellationToken = default);
}
