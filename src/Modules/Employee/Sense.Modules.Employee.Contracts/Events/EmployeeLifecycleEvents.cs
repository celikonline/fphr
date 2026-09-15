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
/// How an employee's contract ended ("İşten ayrılış sihirbazı", 11.3.03 E5a). Mirrors the Domain's own TerminationType
/// enum (Contracts stays independent of Domain, per architecture rule); the Application layer maps between the two.
/// </summary>
public enum TerminationType
{
    ValidCause_Art18 = 0,
    EmployeeJustCause_Art24 = 1,
    EmployerJustCause_Art25 = 2,
    MutualRescission = 3,
    Resignation = 4,
    FixedTermExpiry = 5,
    Other = 6,
}

/// <summary>
/// Raised when an Active/Suspended employee is terminated; the Identity account is not touched by this event.
/// <paramref name="ReasonCode"/> is the chosen TerminationReason's stable Code (not its id - consumers outside
/// Employee never resolve the tenant catalog).
/// </summary>
public sealed record EmployeeTerminated(Guid TenantId, Guid EmployeeId, TerminationType TerminationType, string ReasonCode, string? SgkExitCode) : IntegrationEvent(TenantId);

/// <summary>
/// Raised when a terminated employee is re-hired ("Yeniden işe al", 11.3.03 E5a). <paramref name="LeaveAccrualStartDate"/>
/// and <paramref name="SeniorityStartDate"/> are the merged ("as if worked continuously") dates the rehire settled on:
/// Leave recalculates entitlement from the first, Payroll's severance seniority uses the second. Periods whose leave was
/// settled at exit are already excluded from the first, periods whose severance was paid from the second.
/// </summary>
public sealed record EmployeeRehired(
    Guid TenantId,
    Guid EmployeeId,
    DateOnly HireDate,
    DateOnly LeaveAccrualStartDate,
    DateOnly SeniorityStartDate,
    bool PreviousPeriodsMerged) : IntegrationEvent(TenantId);

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

/// <summary>
/// Raised when a salary record is added, edited (not for a no-op edit) or deleted - Payroll marks open periods dirty
/// from <paramref name="EffectiveFrom"/> on (11.3.07). <paramref name="SalaryRecordId"/> is the affected record (on delete,
/// the removed one). <paramref name="EffectiveFrom"/> is the earliest date whose effective salary changed: the record's
/// date, or the older of the old/new dates when an edit moved it. Consumers read the current values through the
/// Employee module rather than from the event (amounts are sensitive and deliberately not carried).
/// </summary>
public sealed record SalaryChanged(
    Guid TenantId,
    Guid EmployeeId,
    Guid SalaryRecordId,
    DateOnly EffectiveFrom) : IntegrationEvent(TenantId);

/// <summary>Section names carried by <see cref="EmployeeProfileChanged"/>.</summary>
public static class EmployeeProfileSections
{
    /// <summary>Bank accounts (added/edited/deleted/primary changed).</summary>
    public const string Bank = "bank";

    /// <summary>Custom field values changed via the HR/admin write path (PUT /employees/{id}/custom-fields).</summary>
    public const string CustomFields = "custom_fields";

    /// <summary>Self-service edit (PUT /employees/me/profile): contact fields and/or own employee-editable custom field values.</summary>
    public const string SelfService = "self_service";
}

/// <summary>
/// Raised when a section of the employee profile changes (<see cref="EmployeeProfileSections"/>); today only
/// <see cref="EmployeeProfileSections.Bank"/>. Carries no field values (IBANs are sensitive).
/// </summary>
public sealed record EmployeeProfileChanged(Guid TenantId, Guid EmployeeId, string Section) : IntegrationEvent(TenantId);
