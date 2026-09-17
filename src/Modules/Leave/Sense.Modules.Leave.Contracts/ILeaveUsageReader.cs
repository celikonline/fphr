namespace Sense.Modules.Leave.Contracts;

/// <summary>Approved usage of one leave type inside the requested year (S-18); never zero for the types this list includes.</summary>
public sealed record LeaveTypeUsage(Guid LeaveTypeId, string LeaveTypeName, decimal UsedDays);

/// <summary>
/// S-18 (faz-05/faz-10 sözleşmesi, PO sahipliği): annual leave usage summary for one calendar year. <see cref="TotalEntitledDays"/>
/// counts only the Annual-coded leave type - that year's accrual plus the carry-over balance dated inside the year (G-10-09).
/// <see cref="UsedDays"/> is the Annual type's approved usage that year (fractional - half-day/hourly requests count). <see cref="ByLeaveType"/>
/// is approved usage across every leave type that year, regardless of Code.
/// </summary>
public sealed record LeaveUsageSummary(decimal TotalEntitledDays, decimal UsedDays, IReadOnlyList<LeaveTypeUsage> ByLeaveType);

/// <summary>
/// Leave's read port for cross-module annual leave usage reporting (Faz 10's dashboard/KPI is the first consumer). Reads
/// only the ledger's aggregated amounts - no raw LeaveLedgerEntry/LeaveRequest row crosses this boundary.
/// </summary>
public interface ILeaveUsageReader
{
    /// <summary>
    /// <paramref name="employeeIds"/> null = every employee of the tenant.
    /// Year-spanning requests (e.g. Dec 27 - Jan 5) are counted entirely in the start year (first version; day-based split not implemented).
    /// </summary>
    Task<LeaveUsageSummary> GetAnnualUsageAsync(
        Guid tenantId, int year, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);
}
