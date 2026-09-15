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
    DateOnly? HireDate);

/// <summary>Employee card. Health/disability fields and an unmasked NationalId are only populated for callers with employee.sensitive.view; others get a masked NationalId and nulls (Employee.EmployeeSensitiveAccess).</summary>
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
    string? SuspensionReason);

public sealed record DirectoryEntryDto(Guid Id, string DisplayName, Guid? OrgUnitId, string? WorkEmail, string? WorkPhone);

public sealed record OrgChartNodeDto(Guid Id, string DisplayName, Guid? OrgUnitId, IReadOnlyList<OrgChartNodeDto> Children);

public sealed record UpcomingBirthdayDto(Guid Id, string DisplayName, Guid? OrgUnitId, DateOnly NextBirthday);
