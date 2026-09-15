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

/// <summary>Employee card.</summary>
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
    DateTime? LastInvitationSentAt);

public sealed record DirectoryEntryDto(Guid Id, string DisplayName, Guid? OrgUnitId, string? WorkEmail, string? WorkPhone);

public sealed record OrgChartNodeDto(Guid Id, string DisplayName, Guid? OrgUnitId, IReadOnlyList<OrgChartNodeDto> Children);

public sealed record UpcomingBirthdayDto(Guid Id, string DisplayName, Guid? OrgUnitId, DateOnly NextBirthday);
