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
/// or later, on a Draft employee (brief decision #1). LeaveAccrualStartDate is the employee's "İzne esas hak ediş
/// tarihi" (defaults to HireDate, may be earlier for a group transfer/rehire).
/// </summary>
public sealed record EmployeeHired(Guid TenantId, Guid EmployeeId, DateOnly HireDate, DateOnly LeaveAccrualStartDate) : IntegrationEvent(TenantId);

/// <summary>Raised when an Active employee is suspended (askıya alındı); the Identity account is not touched by this event.</summary>
public sealed record EmployeeSuspended(Guid TenantId, Guid EmployeeId, SuspensionReason Reason) : IntegrationEvent(TenantId);

/// <summary>Raised when a Suspended employee is reactivated back to Active.</summary>
public sealed record EmployeeReactivated(Guid TenantId, Guid EmployeeId) : IntegrationEvent(TenantId);

/// <summary>
/// Why a career period starts. Mirrors the Domain's CareerChangeType (Contracts stays independent of Domain, per
/// architecture rule); the Application layer maps between the two.
/// </summary>
public enum CareerChangeType
{
    Hire = 0,
    Promotion = 1,
    LateralMove = 2,
    Demotion = 3,
    TitleChange = 4,
    ManagerChange = 5,
    GroupTransfer = 6,
    BusinessTransfer = 7,
    Rehire = 8,
    Other = 9,
}

/// <summary>
/// Raised whenever the effective manager on an employee's career timeline changes - single edit (PUT /employees/{id}),
/// bulk "Yönetici ata", a career record added/edited/deleted, or the data import. Never raised for a no-op.
/// <paramref name="EffectiveFrom"/> is the career record's ValidFrom and may be in the future: consumers (Workflow
/// reassignment, hierarchy closure refresh) must honour it rather than assume "now".
/// </summary>
public sealed record ManagerChanged(
    Guid TenantId,
    Guid EmployeeId,
    Guid? OldManagerEmployeeId,
    Guid? NewManagerEmployeeId,
    DateOnly EffectiveFrom) : IntegrationEvent(TenantId);

/// <summary>
/// Raised when a career record is added or edited (and when deleting the latest record reopens the previous one, with
/// that record's id/type/date). Feeds the career-path ratio KPI (roadmap #99).
/// </summary>
public sealed record CareerChanged(
    Guid TenantId,
    Guid EmployeeId,
    Guid CareerRecordId,
    CareerChangeType ChangeType,
    DateOnly ValidFrom) : IntegrationEvent(TenantId);
