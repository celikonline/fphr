using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Identity.Contracts;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Employee grid row (web/src/services/employees.service.ts).</summary>
public sealed record EmployeeListItemDto(
    Guid Id,
    string FirstName,
    string LastName,
    string DisplayName,
    string? NationalIdMasked,
    string? WorkEmail,
    string? PersonalEmail,
    string? WorkPhone,
    string Status,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId,
    string? ManagerDisplayName,
    Guid? UserId,
    UserAccountStatus AccountStatus,
    DateOnly? HireDate,
    ContractType? ContractType = null,
    EmploymentType? EmploymentType = null,
    // "Eksik bilgi" badge (EmployeeCompleteness.IsMissingInfo): a non-draft record with an unfilled personnel field.
    bool HasMissingInfo = false);

/// <summary>Employee card. PositionStartDate is derived (ValidFrom of the career record in effect today). DepartmentName/TitleName are resolved from the org placement chain (Department/Title levels) so callers without organization.units.read still see them. Health/disability fields and an unmasked NationalId are only populated for callers with employee.sensitive.view; others get a masked NationalId and nulls (Employee.EmployeeSensitiveAccess).</summary>
public sealed record EmployeeDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string DisplayName,
    string? NationalId,
    string? WorkEmail,
    string? PersonalEmail,
    string? WorkPhone,
    DateOnly? HireDate,
    DateOnly? BirthDate,
    string Status,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId,
    string? ManagerDisplayName,
    Guid? UserId,
    UserAccountStatus AccountStatus,
    string? AccessType,
    DateTime? LastInvitationSentAt,
    Gender Gender,
    MaritalStatus MaritalStatus,
    int? ChildCount,
    EducationStatus? EducationStatus,
    HighestEducationLevel? HighestEducationLevel,
    MilitaryStatus? MilitaryStatus,
    DisabilityDegree? DisabilityDegree,
    BloodType? BloodType,
    string? MobilePhone,
    string? HomePhone,
    string? AddressLine,
    string? City,
    string? PostalCode,
    ContractType? ContractType,
    EmploymentType? EmploymentType,
    string? SuspensionReason,
    DateOnly? LeaveAccrualStartDate = null,
    DateOnly? PositionStartDate = null,
    string? DepartmentName = null,
    string? TitleName = null,
    IReadOnlyList<CustomFieldValueDto>? CustomFields = null,
    Guid? SubcontractorId = null,
    string? SubcontractorName = null,
    DateOnly? TerminationDate = null,
    Guid? TerminationReasonId = null,
    TerminationType? TerminationType = null,
    string? SgkExitCode = null);

/// <summary>
/// Result of the termination wizard's submit step: the updated card plus the advisory job-security warning (never
/// gates the termination - see JobSecurityEvaluator).
/// </summary>
public sealed record TerminateEmployeeResultDto(
    EmployeeDetailDto Employee,
    bool JobSecurityInScope,
    bool JobSecurityWarning,
    string? JobSecurityWarningMessage,
    CollectiveDismissalEvaluation? CollectiveDismissal = null);

public sealed record DirectoryEntryDto(Guid Id, string DisplayName, Guid? OrgUnitId, string? WorkEmail, string? WorkPhone);

public sealed record OrgChartNodeDto(Guid Id, string DisplayName, Guid? OrgUnitId, IReadOnlyList<OrgChartNodeDto> Children);

public sealed record UpcomingBirthdayDto(Guid Id, string DisplayName, Guid? OrgUnitId, DateOnly NextBirthday);

/// <summary>Headcount of active employees under one Department-level org unit ("Çalışan Dağılımı" ana sayfa widget'ı); OrgUnitId/DepartmentName are null for employees with no department assignment.</summary>
public sealed record DepartmentDistributionItemDto(Guid? OrgUnitId, string? DepartmentName, int Count);
