using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>
/// Employee lifecycle status. Quick-add without a hire date creates <see cref="Draft"/>; setting the hire date the
/// first time transitions it to <see cref="Active"/> (Application layer raises the <c>EmployeeHired</c> integration
/// event exactly once for that transition). <see cref="Suspended"/>/<see cref="Terminated"/> are later, explicit
/// transitions (<see cref="Employee.Suspend"/>/<see cref="Employee.Reactivate"/>/<see cref="Employee.Terminate"/>).
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

/// <summary>
/// How an employee's contract ended ("İşten ayrılış sihirbazı", 11.3.03 E5a). A termination reason (Employee.Domain.Terminations.TerminationReason)
/// may suggest a default via its DefaultTerminationType; the wizard lets the user override it.
/// <see cref="ValidCause_Art18"/> is the only value that can trigger the job-security advisory warning
/// (see JobSecurityEvaluator in the Application layer).
/// </summary>
public enum TerminationType
{
    /// <summary>4857 md.18 - işveren feshi, geçerli neden.</summary>
    ValidCause_Art18 = 0,

    /// <summary>4857 md.24 - işçinin haklı nedenle feshi.</summary>
    EmployeeJustCause_Art24 = 1,

    /// <summary>4857 md.25 - işverenin haklı nedenle feshi.</summary>
    EmployerJustCause_Art25 = 2,

    /// <summary>İkale (anlaşmalı fesih).</summary>
    MutualRescission = 3,

    Resignation = 4,
    FixedTermExpiry = 5,
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
    EmploymentType? EmploymentType = null,
    DateOnly? LeaveAccrualStartDate = null);

/// <summary>
/// Employee record (Faz 0: identity/contact fields + org unit + manager hierarchy; Faz 1a adds personal/employment
/// fields and the Draft/Active/Suspended/Terminated status model; career/contract history, salary/bank, custom
/// fields, union and employment periods are later packages).
/// <see cref="UserId"/>, <see cref="OrgUnitId"/> are plain cross-module identifiers (no FK) validated in the Application layer;
/// <see cref="ManagerEmployeeId"/> is same-module but likewise FK-less so the hierarchy check stays a domain rule
/// (<see cref="ManagerHierarchy"/>).
/// </summary>
public sealed partial class Employee : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<CareerRecord> _careerRecords = [];

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

    /// <summary>
    /// "İzne esas hak ediş tarihi" (11.3.03): the date leave entitlement accrues from. Defaults to
    /// <see cref="HireDate"/> the first time a hire date is set, but may be earlier (group transfer, rehire).
    /// </summary>
    public DateOnly? LeaveAccrualStartDate { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public EmployeeStatus Status { get; private set; }

    /// <summary>Set when <see cref="Status"/> is <see cref="EmployeeStatus.Suspended"/>; cleared on <see cref="Reactivate"/>.</summary>
    public SuspensionReason? SuspensionReason { get; private set; }

    public DateTime? SuspendedAtUtc { get; private set; }

    /// <summary>Set once <see cref="Status"/> becomes <see cref="EmployeeStatus.Terminated"/> ("İşten ayrılış sihirbazı", 11.3.03 E5a).</summary>
    public DateOnly? TerminationDate { get; private set; }

    /// <summary>Cross-reference to Employee.Domain.Terminations.TerminationReason (same-module, deliberately FK-less like <see cref="ManagerEmployeeId"/>).</summary>
    public Guid? TerminationReasonId { get; private set; }

    public TerminationType? TerminationType { get; private set; }

    /// <summary>SGK işten çıkış kodu; pre-filled from the chosen reason's default but editable.</summary>
    public string? SgkExitCode { get; private set; }

    /// <summary>
    /// "İşveren vekili" (4857 md.18) - excludes the employee from the job-security scope check regardless of headcount/seniority.
    /// Not yet exposed on the create/update API (Faz 1a E5a gap): set only via <see cref="SetEmployerRepresentative"/>, a later
    /// package should surface it on the employee card.
    /// </summary>
    public bool IsEmployerRepresentative { get; private set; }

    /// <summary>
    /// "Yer altı işçisi" (4857 md.53/md.18). Exists as a field per the 11.3.03 spec; TODO: the statutory job-security rule
    /// also exempts underground workers from the minimum-seniority requirement, which JobSecurityEvaluator does not apply
    /// yet (see its own TODO). Not yet exposed on the create/update API.
    /// </summary>
    public bool IsUndergroundWorker { get; private set; }

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

    /// <summary>
    /// The subcontractor this employee belongs to ("alt işveren çalışanı", 11.3.03 E5b); null for the tenant's own
    /// staff. Same-module, FK-less reference like <see cref="TerminationReasonId"/>; set through <see cref="LinkSubcontractor"/>.
    /// </summary>
    public Guid? SubcontractorId { get; private set; }

    /// <summary>Cross-module reference to Organization.OrgUnit; nullable (an employee may not be assigned to a unit yet).</summary>
    public Guid? OrgUnitId { get; private set; }

    /// <summary>Direct manager (another employee of the same tenant); never self, never cyclic. Denormalized from the career record in effect today.</summary>
    public Guid? ManagerEmployeeId { get; private set; }

    /// <summary>Career periods of this employee (EF navigation; use <see cref="CareerTimeline"/> for the ordered view).</summary>
    public IReadOnlyCollection<CareerRecord> CareerRecords => _careerRecords.AsReadOnly();

    /// <summary>Career records ordered by ValidFrom (oldest first); the last one is the open record.</summary>
    public IReadOnlyList<CareerRecord> CareerTimeline => _careerRecords.OrderBy(r => r.ValidFrom).ThenBy(r => r.Id).ToList();

    /// <summary>True once the employee has anything worth a career record (a unit, a manager or a hire date).</summary>
    public bool HasCareerData => OrgUnitId is not null || ManagerEmployeeId is not null || HireDate is not null;

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
        var previousHireDate = HireDate;

        var hireDateCheck = CheckHireDateAgainstCareer(info.HireDate, previousHireDate);
        if (hireDateCheck.IsFailure)
        {
            return hireDateCheck.Error;
        }

        var applied = Apply(info);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        RealignInitialRecord(previousHireDate);
        Raise(new EmployeeUpdated(Id, TenantId));
        return Result.Success(!wasHired && Status == EmployeeStatus.Active);
    }

    // ------------------------------------------------------------------ career history

    /// <summary>
    /// The record in effect on <paramref name="today"/>: the latest one that has started. When every record is still
    /// in the future (a hire dated ahead of today) the first one is used, so a pre-hire employee already shows the
    /// position they were created with instead of losing their unit/manager.
    /// </summary>
    public CareerRecord? CurrentCareerRecord(DateOnly today)
    {
        var ordered = CareerTimeline;
        if (ordered.Count == 0)
        {
            return null;
        }

        CareerRecord? current = null;
        foreach (var record in ordered.Where(r => r.ValidFrom <= today))
        {
            current = record;
        }

        return current ?? ordered[0];
    }

    /// <summary>"Pozisyon başlama tarihi": ValidFrom of the record in effect today (derived, never stored).</summary>
    public DateOnly? PositionStartDate(DateOnly today) =>
        CurrentCareerRecord(today)?.ValidFrom ?? (HasCareerData ? ImplicitCareerStart(today) : null);

    /// <summary>ValidFrom the (not yet materialized) initial record of a pre-career-history employee gets: its hire date, else the day the record was created.</summary>
    public DateOnly ImplicitCareerStart(DateOnly fallback) =>
        HireDate ?? (CreatedAt == default ? fallback : DateOnly.FromDateTime(CreatedAt));

    /// <summary>
    /// Writes the initial "Hire" record from the current denormalized values when the employee has none yet - both for
    /// a brand new employee and for a record created before career history existed (its id is the employee id, so the
    /// implicit record shown by the read side keeps its identity once materialized). Returns null when nothing was created.
    /// </summary>
    public CareerRecord? EnsureInitialCareerRecord(IReadOnlyList<OrgAssignmentInput> assignments, DateOnly fallbackStart)
    {
        if (_careerRecords.Count > 0 || !HasCareerData)
        {
            return null;
        }

        var record = new CareerRecord(
            Id,
            TenantId,
            Id,
            new CareerRecordData(ImplicitCareerStart(fallbackStart), CareerChangeType.Hire, EmploymentType, ManagerEmployeeId, null, assignments));
        _careerRecords.Add(record);
        return record;
    }

    /// <summary>Adds a career period ("Pozisyon ekle"); closes the previous open record at ValidFrom - 1.</summary>
    public Result<CareerOutcome> AddCareerRecord(CareerRecordData data, CareerContext context)
    {
        var validated = CareerRecord.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var managerCheck = CheckManager(data.ManagerEmployeeId, context);
        if (managerCheck.IsFailure)
        {
            return managerCheck.Error;
        }

        var ordered = CareerTimeline;
        var previous = ordered.Count > 0 ? ordered[^1] : null;
        if (previous is not null && data.ValidFrom <= previous.ValidFrom)
        {
            return Error.Rule(EmployeeDomainErrors.CareerValidFromInvalid);
        }

        if (previous is null && HireDate is { } hireDate && data.ValidFrom < hireDate)
        {
            return Error.Rule(EmployeeDomainErrors.CareerBeforeHireDate);
        }

        previous?.Close(data.ValidFrom.AddDays(-1));
        var record = new CareerRecord(Guid.CreateVersion7(), TenantId, Id, data);
        _careerRecords.Add(record);
        SyncFromCareer(context);

        var transition = previous?.ManagerEmployeeId != data.ManagerEmployeeId
            ? new ManagerTransition(previous?.ManagerEmployeeId, data.ManagerEmployeeId, data.ValidFrom)
            : null;
        return new CareerOutcome(record, Changed: true, transition);
    }

    /// <summary>Edits a career record; only the latest record may move its ValidFrom.</summary>
    public Result<CareerOutcome> UpdateCareerRecord(Guid recordId, CareerRecordData data, CareerContext context)
    {
        var validated = CareerRecord.Validate(data);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var managerCheck = CheckManager(data.ManagerEmployeeId, context);
        if (managerCheck.IsFailure)
        {
            return managerCheck.Error;
        }

        var ordered = CareerTimeline;
        var index = IndexOf(ordered, recordId);
        if (index < 0)
        {
            return Error.NotFound(EmployeeDomainErrors.CareerRecordNotFound);
        }

        var record = ordered[index];
        var previous = index > 0 ? ordered[index - 1] : null;
        if (data.ValidFrom != record.ValidFrom)
        {
            if (index != ordered.Count - 1)
            {
                return Error.Rule(EmployeeDomainErrors.CareerNotLatest);
            }

            if (previous is not null && data.ValidFrom <= previous.ValidFrom)
            {
                return Error.Rule(EmployeeDomainErrors.CareerValidFromInvalid);
            }

            if (previous is null && HireDate is { } hireDate && data.ValidFrom < hireDate)
            {
                return Error.Rule(EmployeeDomainErrors.CareerBeforeHireDate);
            }
        }

        var oldManager = record.ManagerEmployeeId;
        var oldValidFrom = record.ValidFrom;
        var changed = record.Apply(data);
        previous?.Close(record.ValidFrom.AddDays(-1));
        SyncFromCareer(context);

        ManagerTransition? transition = null;
        if (oldManager != record.ManagerEmployeeId)
        {
            transition = new ManagerTransition(oldManager, record.ManagerEmployeeId, record.ValidFrom);
        }
        else if (oldValidFrom != record.ValidFrom && previous?.ManagerEmployeeId != record.ManagerEmployeeId)
        {
            // Same manager, different start date: the timeline's change point moved.
            transition = new ManagerTransition(previous?.ManagerEmployeeId, record.ManagerEmployeeId, record.ValidFrom);
        }

        return new CareerOutcome(record, changed, transition);
    }

    /// <summary>Deletes the latest career record and reopens the previous one; the only record of an employee cannot be deleted (edit it instead).</summary>
    public Result<CareerOutcome> DeleteCareerRecord(Guid recordId, CareerContext context)
    {
        var ordered = CareerTimeline;
        var index = IndexOf(ordered, recordId);
        if (index < 0)
        {
            return Error.NotFound(EmployeeDomainErrors.CareerRecordNotFound);
        }

        if (index != ordered.Count - 1)
        {
            return Error.Rule(EmployeeDomainErrors.CareerNotLatest);
        }

        if (ordered.Count == 1)
        {
            return Error.Rule(EmployeeDomainErrors.CareerLastRecord);
        }

        var record = ordered[index];
        var previous = ordered[index - 1];
        _careerRecords.Remove(record);
        previous.Reopen(record.ValidTo);
        SyncFromCareer(context);

        var transition = record.ManagerEmployeeId != previous.ManagerEmployeeId
            ? new ManagerTransition(record.ManagerEmployeeId, previous.ManagerEmployeeId, record.ValidFrom)
            : null;
        return new CareerOutcome(previous, Changed: true, transition);
    }

    /// <summary>
    /// Placement change coming from a non-career write path (PUT /employees/{id}, bulk assign-manager, import):
    /// amends the latest record when it starts today or later, otherwise opens a new record effective today, so the
    /// history and the denormalized fields can never diverge.
    /// </summary>
    public Result<CareerOutcome> ApplyPlacement(PlacementRequest request, CareerContext context)
    {
        var ordered = CareerTimeline;
        var latest = ordered.Count > 0 ? ordered[^1] : null;
        if (latest is null)
        {
            return AddCareerRecord(
                new CareerRecordData(HireDate ?? context.Today, CareerChangeType.Hire, request.EmploymentType, request.ManagerEmployeeId, null, request.Assignments),
                context);
        }

        if (latest.ManagerEmployeeId == request.ManagerEmployeeId &&
            latest.EmploymentType == request.EmploymentType &&
            latest.HasSameAssignments(request.Assignments))
        {
            return new CareerOutcome(latest, Changed: false);
        }

        return latest.ValidFrom >= context.Today
            ? UpdateCareerRecord(
                latest.Id,
                new CareerRecordData(latest.ValidFrom, latest.ChangeType, request.EmploymentType, request.ManagerEmployeeId, latest.Note, request.Assignments),
                context)
            : AddCareerRecord(
                new CareerRecordData(context.Today, request.ChangeType, request.EmploymentType, request.ManagerEmployeeId, null, request.Assignments),
                context);
    }

    /// <summary>Denormalizes the record in effect today onto the employee (unit = deepest level, manager, employment type).</summary>
    private void SyncFromCareer(CareerContext context)
    {
        if (CurrentCareerRecord(context.Today) is not { } current)
        {
            return;
        }

        AssignOrgUnit(current.DeepestOrgUnitId(context.LevelOrder));

        if (ManagerEmployeeId != current.ManagerEmployeeId)
        {
            ManagerEmployeeId = current.ManagerEmployeeId;
            Raise(new EmployeeManagerChanged(Id, TenantId, current.ManagerEmployeeId));
        }

        EmploymentType = current.EmploymentType;
    }

    private Result CheckManager(Guid? managerEmployeeId, CareerContext context)
    {
        if (managerEmployeeId is not { } managerId)
        {
            return Result.Success();
        }

        if (managerId == Id)
        {
            return Error.Rule(EmployeeDomainErrors.ManagerSelf);
        }

        return ManagerHierarchy.WouldCreateCycle(Id, managerId, context.ManagerOf)
            ? Error.Rule(EmployeeDomainErrors.ManagerCycle)
            : Result.Success();
    }

    /// <summary>PositionStartDate &gt;= HireDate (11.3.03): a hire date after the first record is only accepted when that record can follow it.</summary>
    private Result CheckHireDateAgainstCareer(DateOnly? newHireDate, DateOnly? oldHireDate)
    {
        var ordered = CareerTimeline;
        if (ordered.Count == 0 || newHireDate is not { } hireDate || hireDate <= ordered[0].ValidFrom)
        {
            return Result.Success();
        }

        return CanRealignInitialRecord(ordered, hireDate, oldHireDate)
            ? Result.Success()
            : Error.Rule(EmployeeDomainErrors.CareerBeforeHireDate);
    }

    /// <summary>Keeps an untouched initial Hire record on the hire date when that date is corrected.</summary>
    private void RealignInitialRecord(DateOnly? oldHireDate)
    {
        var ordered = CareerTimeline;
        if (ordered.Count == 0 || HireDate is not { } hireDate || hireDate == ordered[0].ValidFrom)
        {
            return;
        }

        if (CanRealignInitialRecord(ordered, hireDate, oldHireDate))
        {
            ordered[0].SetValidFrom(hireDate);
        }
    }

    private static bool CanRealignInitialRecord(IReadOnlyList<CareerRecord> ordered, DateOnly hireDate, DateOnly? oldHireDate)
    {
        var first = ordered[0];
        var untouched = first.ChangeType == CareerChangeType.Hire && (oldHireDate is null || first.ValidFrom == oldHireDate);
        return untouched && (ordered.Count == 1 || hireDate < ordered[1].ValidFrom);
    }

    private static int IndexOf(IReadOnlyList<CareerRecord> ordered, Guid recordId)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Id == recordId)
            {
                return i;
            }
        }

        return -1;
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

    /// <summary>Links (or unlinks, with null) the subcontractor this employee works for; the Application layer checks the id exists and is active.</summary>
    public void LinkSubcontractor(Guid? subcontractorId) => SubcontractorId = subcontractorId;

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

    /// <summary>
    /// Terminates an Active or Suspended employee ("İşten ayrılış sihirbazı", 11.3.03 E5a); does not touch the Identity
    /// account. <paramref name="terminationReasonId"/> is validated for existence by the Application layer (same-module
    /// FK-less reference, per <see cref="TerminationReasonId"/>).
    /// </summary>
    public Result Terminate(
        DateOnly terminationDate,
        Guid terminationReasonId,
        TerminationType terminationType,
        string? sgkExitCode,
        bool severancePaid = false,
        bool leaveSettledAtExit = false)
    {
        if (Status is not (EmployeeStatus.Active or EmployeeStatus.Suspended))
        {
            return Error.Rule(EmployeeDomainErrors.TerminateInvalidStatus);
        }

        if (HireDate is { } hireDate && terminationDate < hireDate)
        {
            return Error.Rule(EmployeeDomainErrors.TerminationDateBeforeHireDate);
        }

        // Settlement flags close the open employment period (no-op when periods were not loaded, see Employee.EmploymentPeriods).
        CloseOpenEmploymentPeriod(terminationDate, terminationReasonId, severancePaid, leaveSettledAtExit);

        TerminationDate = terminationDate;
        TerminationReasonId = terminationReasonId;
        TerminationType = terminationType;
        SgkExitCode = string.IsNullOrWhiteSpace(sgkExitCode) ? null : sgkExitCode.Trim();
        SuspensionReason = null;
        SuspendedAtUtc = null;
        Status = EmployeeStatus.Terminated;
        Raise(new EmployeeStatusChanged(Id, TenantId, Status));
        return Result.Success();
    }

    /// <summary>See <see cref="IsEmployerRepresentative"/> doc comment for why this is a standalone setter rather than part of <see cref="Apply"/>.</summary>
    public void SetEmployerRepresentative(bool value) => IsEmployerRepresentative = value;

    /// <summary>See <see cref="IsUndergroundWorker"/> doc comment for why this is a standalone setter rather than part of <see cref="Apply"/>.</summary>
    public void SetUndergroundWorker(bool value) => IsUndergroundWorker = value;

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
        var previousHireDate = HireDate;
        HireDate = info.HireDate;
        LeaveAccrualStartDate = ResolveLeaveAccrualStart(info.LeaveAccrualStartDate, LeaveAccrualStartDate, previousHireDate, HireDate);
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

        // Once career history exists the employment type belongs to the career record; the placement path
        // (ApplyPlacement) writes it there and SyncFromCareer copies it back onto the employee.
        if (_careerRecords.Count == 0)
        {
            EmploymentType = info.EmploymentType;
        }

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

    /// <summary>
    /// "İzne esas hak ediş tarihi": an explicit value always wins; otherwise it defaults to the hire date the first
    /// time one is set and follows a later hire-date correction only while it still equals the old hire date (a value
    /// deliberately moved earlier - group transfer, rehire - is kept). A termination-date upper bound comes with the
    /// termination package (E4), which owns that field.
    /// </summary>
    private static DateOnly? ResolveLeaveAccrualStart(DateOnly? requested, DateOnly? current, DateOnly? oldHireDate, DateOnly? newHireDate)
    {
        if (requested is not null)
        {
            return requested;
        }

        if (current is null)
        {
            return newHireDate;
        }

        return oldHireDate is not null && current == oldHireDate && newHireDate is not null ? newHireDate : current;
    }
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
