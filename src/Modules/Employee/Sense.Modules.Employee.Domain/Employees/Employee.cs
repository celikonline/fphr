using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Employee lifecycle status. Quick-add without a hire date creates <see cref="Draft"/>; setting the hire date the
/// first time transitions it to <see cref="Active"/> (Application layer raises the <c>EmployeeHired</c> integration
/// event exactly once for that transition). <see cref="Suspended"/>/<see cref="Terminated"/> are later, explicit
/// transitions (<see cref="Employee.Suspend"/>/<see cref="Employee.Reactivate"/>; termination is a later package).
/// </summary>
public enum EmployeeStatus
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Terminated = 3,
}

/// <summary>Why an employee is currently <see cref="EmployeeStatus.Suspended"/> (askıya alma nedeni, 11.3.03).</summary>
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

/// <summary>Editable personal/contact/employment fields of an employee, validated together by <see cref="Employee.Create"/> and <see cref="Employee.UpdateInfo"/>.</summary>
public sealed record EmployeeInfo(
    string FirstName,
    string LastName,
    string? NationalId = null,
    string? WorkEmail = null,
    string? PersonalEmail = null,
    string? WorkPhone = null,
    DateOnly? HireDate = null,
    DateOnly? BirthDate = null,
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
    EmploymentType? EmploymentType = null);

/// <summary>
/// Employee record (Faz 0: identity/contact fields + org unit + manager hierarchy; Faz 1a adds personal/employment
/// fields and the Draft/Active/Suspended/Terminated status model; career/contract history, salary/bank, custom
/// fields, union and employment periods are later packages).
/// <see cref="UserId"/>, <see cref="OrgUnitId"/> are plain cross-module identifiers (no FK) validated in the Application layer;
/// <see cref="ManagerEmployeeId"/> is same-module but likewise FK-less so the hierarchy check stays a domain rule
/// (<see cref="ManagerHierarchy"/>).
/// </summary>
public sealed class Employee : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Employee()
    {
    }

    private Employee(Guid id, Guid tenantId, Guid? userId, Guid? orgUnitId)
        : base(id, tenantId)
    {
        UserId = userId;
        OrgUnitId = orgUnitId;
        Status = EmployeeStatus.Draft;
    }

    /// <summary>Cross-module reference to Identity.User; nullable because an employee record can exist before a login account.</summary>
    public Guid? UserId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    /// <summary>"First Last", recomputed whenever the names change.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>11-digit T.C. Kimlik No (validated with <see cref="TcKimlik"/>); unique per tenant when set.</summary>
    public string? NationalId { get; private set; }

    public string? WorkEmail { get; private set; }

    public string? PersonalEmail { get; private set; }

    public string? WorkPhone { get; private set; }

    public DateOnly? HireDate { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public EmployeeStatus Status { get; private set; }

    /// <summary>Set when <see cref="Status"/> is <see cref="EmployeeStatus.Suspended"/>; cleared on <see cref="Reactivate"/>.</summary>
    public SuspensionReason? SuspensionReason { get; private set; }

    public DateTime? SuspendedAtUtc { get; private set; }

    public Gender Gender { get; private set; }

    public MaritalStatus MaritalStatus { get; private set; }

    public int? ChildCount { get; private set; }

    public EducationStatus? EducationStatus { get; private set; }

    public HighestEducationLevel? HighestEducationLevel { get; private set; }

    /// <summary>Sensitive: only exposed with employee.sensitive.view (Application layer masks it otherwise).</summary>
    public MilitaryStatus? MilitaryStatus { get; private set; }

    /// <summary>Sensitive: only exposed with employee.sensitive.view (Application layer masks it otherwise).</summary>
    public DisabilityDegree? DisabilityDegree { get; private set; }

    /// <summary>Sensitive: only exposed with employee.sensitive.view (Application layer masks it otherwise).</summary>
    public BloodType? BloodType { get; private set; }

    public string? MobilePhone { get; private set; }

    public string? HomePhone { get; private set; }

    public string? AddressLine { get; private set; }

    public string? City { get; private set; }

    public string? PostalCode { get; private set; }

    public ContractType? ContractType { get; private set; }

    public EmploymentType? EmploymentType { get; private set; }

    /// <summary>Cross-module reference to Organization.OrgUnit; nullable (an employee may not be assigned to a unit yet).</summary>
    public Guid? OrgUnitId { get; private set; }

    /// <summary>Direct manager (another employee of the same tenant); never self, never cyclic.</summary>
    public Guid? ManagerEmployeeId { get; private set; }

    public static Result<Employee> Create(Guid tenantId, EmployeeInfo info, Guid? userId = null, Guid? orgUnitId = null, Guid? managerEmployeeId = null, Guid? id = null)
    {
        var employee = new Employee(id ?? Guid.CreateVersion7(), tenantId, userId, orgUnitId);

        var applied = employee.Apply(info);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        if (managerEmployeeId is { } managerId)
        {
            if (managerId == employee.Id)
            {
                return Error.Rule(EmployeeDomainErrors.ManagerSelf);
            }

            employee.ManagerEmployeeId = managerId;
        }

        employee.Raise(new EmployeeCreated(employee.Id, tenantId, employee.DisplayName, userId, orgUnitId));
        return employee;
    }

    /// <summary>True if this call transitioned the employee from Draft (no hire date) to Active (hire date set) - the caller enqueues EmployeeHired exactly once for that transition.</summary>
    public Result<bool> UpdateInfo(EmployeeInfo info)
    {
        var wasHired = Status == EmployeeStatus.Active;
        var applied = Apply(info);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        Raise(new EmployeeUpdated(Id, TenantId));
        return Result.Success(!wasHired && Status == EmployeeStatus.Active);
    }

    /// <summary>Assigns (or clears) the direct manager. <paramref name="managerOf"/> maps every employee id of the tenant to its current manager (used to detect cycles).</summary>
    public Result AssignManager(Guid? managerEmployeeId, IReadOnlyDictionary<Guid, Guid?> managerOf)
    {
        if (managerEmployeeId is { } managerId)
        {
            if (managerId == Id)
            {
                return Error.Rule(EmployeeDomainErrors.ManagerSelf);
            }

            if (ManagerHierarchy.WouldCreateCycle(Id, managerId, managerOf))
            {
                return Error.Rule(EmployeeDomainErrors.ManagerCycle);
            }
        }

        if (ManagerEmployeeId != managerEmployeeId)
        {
            ManagerEmployeeId = managerEmployeeId;
            Raise(new EmployeeManagerChanged(Id, TenantId, managerEmployeeId));
        }

        return Result.Success();
    }

    public void AssignOrgUnit(Guid? orgUnitId)
    {
        if (OrgUnitId == orgUnitId)
        {
            return;
        }

        OrgUnitId = orgUnitId;
        Raise(new EmployeeOrgUnitChanged(Id, TenantId, orgUnitId));
    }

    public void LinkUser(Guid userId) => UserId = userId;

    public void SetStatus(EmployeeStatus status)
    {
        Status = status;
        Raise(new EmployeeStatusChanged(Id, TenantId, status));
    }

    /// <summary>Suspends an Active employee (askıya alma, 11.3.03); does not touch the Identity account.</summary>
    public Result Suspend(SuspensionReason reason, DateTime nowUtc)
    {
        if (Status != EmployeeStatus.Active)
        {
            return Error.Rule(EmployeeDomainErrors.SuspendInvalidStatus);
        }

        SuspensionReason = reason;
        SuspendedAtUtc = nowUtc;
        Status = EmployeeStatus.Suspended;
        Raise(new EmployeeStatusChanged(Id, TenantId, Status));
        return Result.Success();
    }

    /// <summary>Reactivates a Suspended employee back to Active.</summary>
    public Result Reactivate()
    {
        if (Status != EmployeeStatus.Suspended)
        {
            return Error.Rule(EmployeeDomainErrors.ReactivateNotSuspended);
        }

        SuspensionReason = null;
        SuspendedAtUtc = null;
        Status = EmployeeStatus.Active;
        Raise(new EmployeeStatusChanged(Id, TenantId, Status));
        return Result.Success();
    }

    /// <summary>Last 4 digits only, e.g. "*******1234".</summary>
    public static string? MaskNationalId(string? nationalId) =>
        string.IsNullOrEmpty(nationalId) || nationalId.Length <= 4
            ? nationalId
            : string.Concat(new string('*', nationalId.Length - 4), nationalId.AsSpan(nationalId.Length - 4));

    public static string ComposeDisplayName(string firstName, string lastName) => string.Concat(firstName.Trim(), " ", lastName.Trim()).Trim();

    private Result Apply(EmployeeInfo info)
    {
        var validation = Validate(info);
        if (validation.IsFailure)
        {
            return validation;
        }

        FirstName = info.FirstName.Trim();
        LastName = info.LastName.Trim();
        DisplayName = ComposeDisplayName(FirstName, LastName);
        NationalId = Normalize(info.NationalId);
        WorkEmail = Normalize(info.WorkEmail)?.ToLowerInvariant();
        PersonalEmail = Normalize(info.PersonalEmail)?.ToLowerInvariant();
        WorkPhone = Normalize(info.WorkPhone);
        HireDate = info.HireDate;
        BirthDate = info.BirthDate;
        Gender = info.Gender;
        MaritalStatus = info.MaritalStatus;
        ChildCount = info.ChildCount;
        EducationStatus = info.EducationStatus;
        HighestEducationLevel = info.HighestEducationLevel;
        MilitaryStatus = info.MilitaryStatus;
        DisabilityDegree = info.DisabilityDegree;
        BloodType = info.BloodType;
        MobilePhone = PhoneNormalizer.Normalize(info.MobilePhone);
        HomePhone = PhoneNormalizer.Normalize(info.HomePhone);
        AddressLine = Normalize(info.AddressLine);
        City = Normalize(info.City);
        PostalCode = Normalize(info.PostalCode);
        ContractType = info.ContractType;
        EmploymentType = info.EmploymentType;

        // Draft -> Active the first time a hire date is set; later statuses (Suspended/Terminated) are not
        // reopened by a hire date edit alone.
        if (Status == EmployeeStatus.Draft && HireDate is not null)
        {
            Status = EmployeeStatus.Active;
        }

        return Result.Success();
    }

    public static Result Validate(EmployeeInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.FirstName))
        {
            return Error.Validation(EmployeeDomainErrors.FirstNameRequired);
        }

        if (string.IsNullOrWhiteSpace(info.LastName))
        {
            return Error.Validation(EmployeeDomainErrors.LastNameRequired);
        }

        if (info.FirstName.Trim().Length > EmployeeLimits.NameMaxLength || info.LastName.Trim().Length > EmployeeLimits.NameMaxLength)
        {
            return Error.Validation(EmployeeDomainErrors.NameTooLong);
        }

        if (Normalize(info.NationalId) is { } nationalId && !TcKimlik.IsValid(nationalId))
        {
            return Error.Validation(EmployeeDomainErrors.NationalIdInvalid);
        }

        if (Normalize(info.WorkEmail) is { } workEmail && !EmailAddress.IsValid(workEmail))
        {
            return Error.Validation(EmployeeDomainErrors.WorkEmailInvalid);
        }

        if (Normalize(info.PersonalEmail) is { } personalEmail && !EmailAddress.IsValid(personalEmail))
        {
            return Error.Validation(EmployeeDomainErrors.PersonalEmailInvalid);
        }

        if (Normalize(info.WorkPhone) is { Length: > EmployeeLimits.PhoneMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.WorkPhoneTooLong);
        }

        if (info.ChildCount is < 0)
        {
            return Error.Validation(EmployeeDomainErrors.ChildCountInvalid);
        }

        if (PhoneNormalizer.Normalize(info.MobilePhone) is { Length: > EmployeeLimits.PhoneMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.MobilePhoneTooLong);
        }

        if (PhoneNormalizer.Normalize(info.HomePhone) is { Length: > EmployeeLimits.PhoneMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.HomePhoneTooLong);
        }

        if (Normalize(info.AddressLine) is { Length: > EmployeeLimits.AddressLineMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.AddressLineTooLong);
        }

        if (Normalize(info.City) is { Length: > EmployeeLimits.CityMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.CityTooLong);
        }

        if (Normalize(info.PostalCode) is { Length: > EmployeeLimits.PostalCodeMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.PostalCodeTooLong);
        }

        return Result.Success();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Manager hierarchy rules shared by single and bulk manager assignment.</summary>
public static class ManagerHierarchy
{
    /// <summary>True if making <paramref name="managerId"/> the manager of <paramref name="employeeId"/> closes a loop (walks the new manager's chain upward).</summary>
    public static bool WouldCreateCycle(Guid employeeId, Guid managerId, IReadOnlyDictionary<Guid, Guid?> managerOf)
    {
        var visited = new HashSet<Guid>();
        Guid? current = managerId;
        while (current is { } node)
        {
            if (node == employeeId)
            {
                return true;
            }

            // Guards against a pre-existing loop elsewhere in the data.
            if (!visited.Add(node))
            {
                return false;
            }

            current = managerOf.TryGetValue(node, out var next) ? next : null;
        }

        return false;
    }

    /// <summary>All direct and indirect reports of <paramref name="managerId"/> (excluding the manager).</summary>
    public static IReadOnlySet<Guid> ReportsOf(Guid managerId, IReadOnlyDictionary<Guid, Guid?> managerOf)
    {
        var reportsByManager = managerOf.Where(kv => kv.Value is not null).ToLookup(kv => kv.Value!.Value, kv => kv.Key);
        var result = new HashSet<Guid>();
        var pending = new Queue<Guid>();
        pending.Enqueue(managerId);
        while (pending.TryDequeue(out var current))
        {
            foreach (var report in reportsByManager[current])
            {
                if (report != managerId && result.Add(report))
                {
                    pending.Enqueue(report);
                }
            }
        }

        return result;
    }
}

public sealed record EmployeeCreated(Guid EmployeeId, Guid TenantId, string DisplayName, Guid? UserId, Guid? OrgUnitId) : DomainEvent;

public sealed record EmployeeUpdated(Guid EmployeeId, Guid TenantId) : DomainEvent;

public sealed record EmployeeOrgUnitChanged(Guid EmployeeId, Guid TenantId, Guid? OrgUnitId) : DomainEvent;

public sealed record EmployeeManagerChanged(Guid EmployeeId, Guid TenantId, Guid? ManagerEmployeeId) : DomainEvent;

public sealed record EmployeeStatusChanged(Guid EmployeeId, Guid TenantId, EmployeeStatus Status) : DomainEvent;
