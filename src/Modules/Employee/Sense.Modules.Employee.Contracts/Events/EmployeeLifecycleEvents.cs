using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Employee.Contracts.Events;

/// <summary>
/// Employee lifecycle reasons for a Suspended status (askıya alma nedeni, 11.3.03). Mirrors the Domain's own
/// SuspensionReason enum; Contracts stays independent of Domain (architecture rule), so the Application layer maps
/// between the two when raising <see cref="EmployeeSuspended"/>.
/// </summary>
public enum SuspensionReason
{
    MilitaryService = 0,
    UnionOffice = 1,
    LongUnpaidLeave = 2,
    Detention = 3,
    HealthReport = 4,
    ForceMajeure = 5,
    Other = 6,
}

/// <summary>
/// Raised (via Employee's outbox) exactly once, the first time an employee's hire date is set - whether at creation
/// or later, on a Draft employee (brief decision #1). LeaveAccrualStartDate = HireDate until the Leave module has
/// its own accrual-start field.
/// </summary>
public sealed record EmployeeHired(Guid TenantId, Guid EmployeeId, DateOnly HireDate, DateOnly LeaveAccrualStartDate) : IntegrationEvent(TenantId);

/// <summary>Raised when an Active employee is suspended (askıya alındı); the Identity account is not touched by this event.</summary>
public sealed record EmployeeSuspended(Guid TenantId, Guid EmployeeId, SuspensionReason Reason) : IntegrationEvent(TenantId);

/// <summary>Raised when a Suspended employee is reactivated back to Active.</summary>
public sealed record EmployeeReactivated(Guid TenantId, Guid EmployeeId) : IntegrationEvent(TenantId);
