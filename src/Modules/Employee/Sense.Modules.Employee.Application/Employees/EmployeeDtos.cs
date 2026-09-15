namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Minimal employee shape (Faz 0 stub): id, display name, status, org unit, linked user.</summary>
public sealed record EmployeeListItemDto(Guid Id, string DisplayName, string Status, Guid? OrgUnitId, Guid? UserId);
