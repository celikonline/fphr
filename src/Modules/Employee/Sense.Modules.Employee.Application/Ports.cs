using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Paging;

namespace Sense.Modules.Employee.Application;

/// <summary>
/// Resolved employee query criteria. Null collections mean "no restriction"; an empty collection matches nothing.
/// <see cref="VisibleIds"/> carries the caller's data scope (IDataScopeService; null = Company scope).
/// </summary>
public sealed record EmployeeCriteria(
    string? Q = null,
    IReadOnlyCollection<Guid>? Ids = null,
    IReadOnlyCollection<Guid>? OrgUnitIds = null,
    EmployeeStatus? Status = null,
    bool? HasUserAccount = null,
    Guid? ManagerEmployeeId = null,
    IReadOnlySet<Guid>? VisibleIds = null,
    bool ActiveOnly = false,
    bool SearchNationalId = true);

/// <summary>Flat read model of an employee row.</summary>
public sealed record EmployeeRow(
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
    EmployeeStatus Status,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId,
    Guid? UserId)
{
    public static EmployeeRow From(Domain.Employees.Employee e) => new(
        e.Id, e.FirstName, e.LastName, e.DisplayName, e.NationalId, e.WorkEmail, e.PersonalEmail, e.WorkPhone,
        e.HireDate, e.BirthDate, e.Status, e.OrgUnitId, e.ManagerEmployeeId, e.UserId);
}

/// <summary>Grid sort field names accepted by the employee list (?sort=).</summary>
public static class EmployeeSortFields
{
    public const string DisplayName = "displayName";
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string HireDate = "hireDate";
    public const string Status = "status";
}

/// <summary>Read side (EF projections in Infrastructure) for list/directory/org-chart/birthday screens.</summary>
public interface IEmployeeReadStore
{
    Task<PagedResult<EmployeeRow>> ListAsync(EmployeeCriteria criteria, PagedQuery paging, CancellationToken ct);

    Task<IReadOnlyList<Guid>> ListIdsAsync(EmployeeCriteria criteria, CancellationToken ct);

    Task<IReadOnlyList<EmployeeRow>> ListAllAsync(EmployeeCriteria criteria, CancellationToken ct);

    Task<EmployeeRow?> GetAsync(Guid id, CancellationToken ct);

    Task<EmployeeRow?> GetByUserIdAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

/// <summary>Persists pending Employee changes outside an Employee command pipeline (e.g. rows applied by the data import).</summary>
public interface IEmployeeUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
