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
    bool SearchNationalId = true,
    EmploymentType? EmploymentType = null,
    ContractType? ContractType = null,
    bool? MissingInfo = null);

/// <summary>
/// "Eksik bilgi" (11.3.03 E7): the personnel fields an employee record is expected to carry once it is real. A Draft
/// row (quick-add, no hire date yet) is not flagged - it is knowingly incomplete until someone hires them.
/// </summary>
public static class EmployeeCompleteness
{
    public static bool IsMissingInfo(EmployeeRow e) => MissingFieldCount(e) > 0;

    /// <summary>How many of the expected personnel fields are unfilled (badge text, e.g. "Eksik bilgi (2)") - 0 for a
    /// Draft row, which is knowingly incomplete until someone hires them.</summary>
    public static int MissingFieldCount(EmployeeRow e)
    {
        if (e.Status == EmployeeStatus.Draft)
        {
            return 0;
        }

        var count = 0;
        if (e.NationalId is null)
        {
            count++;
        }

        if (e.HireDate is null)
        {
            count++;
        }

        if (e.BirthDate is null)
        {
            count++;
        }

        if (e.OrgUnitId is null)
        {
            count++;
        }

        if (e.ContractType is null)
        {
            count++;
        }

        if (e.EmploymentType is null)
        {
            count++;
        }

        return count;
    }
}

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
    Guid? UserId,
    Gender Gender = Gender.Unspecified,
    MaritalStatus MaritalStatus = MaritalStatus.Unspecified,
    int? ChildCount = null,
    EducationStatus? EducationStatus = null,
    HighestEducationLevel? HighestEducationLevel = null,
    MilitaryStatus? MilitaryStatus = null,
    DisabilityDegree? DisabilityDegree = null,
    BloodType? BloodType = null,
    string? MobilePhone = null,
    string? HomePhone = null,
    string? AddressLine = null,
    string? City = null,
    string? PostalCode = null,
    ContractType? ContractType = null,
    EmploymentType? EmploymentType = null,
    SuspensionReason? SuspensionReason = null,
    DateOnly? LeaveAccrualStartDate = null,
    DateOnly? PositionStartDate = null,
    DateOnly? TerminationDate = null,
    Guid? TerminationReasonId = null,
    TerminationType? TerminationType = null,
    string? SgkExitCode = null,
    Guid? SubcontractorId = null,
    string? SicilNumarasi = null,
    Nationality Nationality = Nationality.Turkish,
    string? ForeignIdNumber = null,
    string? PassportNumber = null,
    string? SgkOccupationCode = null)
{
    /// <summary>
    /// <paramref name="today"/> resolves the derived PositionStartDate (ValidFrom of the career record in effect
    /// today); pass null where career records were not loaded (grid/list projections don't show it).
    /// </summary>
    public static EmployeeRow From(Domain.Employees.Employee e, DateOnly? today = null) => new(
        e.Id, e.FirstName, e.LastName, e.DisplayName, e.NationalId, e.WorkEmail, e.PersonalEmail, e.WorkPhone,
        e.HireDate, e.BirthDate, e.Status, e.OrgUnitId, e.ManagerEmployeeId, e.UserId,
        e.Gender, e.MaritalStatus, e.ChildCount, e.EducationStatus, e.HighestEducationLevel, e.MilitaryStatus, e.DisabilityDegree, e.BloodType,
        e.MobilePhone, e.HomePhone, e.AddressLine, e.City, e.PostalCode, e.ContractType, e.EmploymentType, e.SuspensionReason,
        e.LeaveAccrualStartDate, today is { } date ? e.PositionStartDate(date) : null,
        e.TerminationDate, e.TerminationReasonId, e.TerminationType, e.SgkExitCode, e.SubcontractorId,
        e.SicilNumarasi, e.Nationality, e.ForeignIdNumber, e.PassportNumber, e.SgkOccupationCode);
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

    /// <summary>Untracked employee with its career records - the career read side reuses the aggregate's own timeline rules (current record, implicit initial record).</summary>
    Task<Domain.Employees.Employee?> GetWithCareerAsync(Guid id, CancellationToken ct);

    /// <summary>Untracked employee with its salary records and bank accounts (the payment reads reuse the aggregate's current-salary / ordering rules).</summary>
    Task<Domain.Employees.Employee?> GetWithPaymentsAsync(Guid id, CancellationToken ct);

    /// <summary>Untracked employee with its employment periods (the seniority reads reuse the aggregate's own counting rules).</summary>
    Task<Domain.Employees.Employee?> GetWithEmploymentPeriodsAsync(Guid id, CancellationToken ct);

    /// <summary>Untracked employee with its union memberships (special-category data; only the union reads load it).</summary>
    Task<Domain.Employees.Employee?> GetWithUnionAsync(Guid id, CancellationToken ct);

    /// <summary>Stored custom field values of one employee, keyed by CustomFieldDefinition id.</summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetCustomFieldValuesAsync(Guid employeeId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

/// <summary>Persists pending Employee changes outside an Employee command pipeline (e.g. rows applied by the data import).</summary>
public interface IEmployeeUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
