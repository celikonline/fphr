using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>Why a career record starts (11.3.03 "Kariyer kaydı"); feeds the career-path ratio KPI (roadmap #36/#99).</summary>
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
/// The employee's unit on one hierarchy level (OrgUnitType) while a career record is in effect - 11.3.02's
/// "çalışanın konumu seviye başına bir birimden oluşan demet". At most one per level per record.
/// </summary>
public sealed class EmployeeOrgAssignment
{
    public EmployeeOrgAssignment(Guid orgUnitTypeId, Guid orgUnitId)
    {
        OrgUnitTypeId = orgUnitTypeId;
        OrgUnitId = orgUnitId;
    }

    private EmployeeOrgAssignment()
    {
    }

    /// <summary>Cross-module reference to Organization.OrgUnitType (the level).</summary>
    public Guid OrgUnitTypeId { get; private set; }

    /// <summary>Cross-module reference to Organization.OrgUnit; always of <see cref="OrgUnitTypeId"/> (validated in the Application layer).</summary>
    public Guid OrgUnitId { get; private set; }

    // In-place move (instead of replacing the owned instance) so EF writes an UPDATE, not a delete+insert pair.
    internal void MoveTo(Guid orgUnitId) => OrgUnitId = orgUnitId;
}

/// <summary>One assignment as sent by a caller (level + unit).</summary>
public sealed record OrgAssignmentInput(Guid OrgUnitTypeId, Guid OrgUnitId);

/// <summary>Editable content of a career record.</summary>
public sealed record CareerRecordData(
    DateOnly ValidFrom,
    CareerChangeType ChangeType,
    EmploymentType? EmploymentType = null,
    Guid? ManagerEmployeeId = null,
    string? Note = null,
    IReadOnlyList<OrgAssignmentInput>? Assignments = null)
{
    public IReadOnlyList<OrgAssignmentInput> AssignmentList => Assignments ?? [];
}

/// <summary>
/// Everything a career mutation needs from the outside: "today" (UTC, see <see cref="Employee.CurrentCareerRecord"/>),
/// the tenant's level order (OrgUnitType.SortOrder, for the deepest-unit denormalization) and the manager map used by
/// the existing cycle check.
/// </summary>
public sealed record CareerContext(
    DateOnly Today,
    IReadOnlyDictionary<Guid, int> LevelOrder,
    IReadOnlyDictionary<Guid, Guid?> ManagerOf);

/// <summary>Effective-manager change on the career timeline; the Application layer turns it into the ManagerChanged integration event.</summary>
public sealed record ManagerTransition(Guid? OldManagerEmployeeId, Guid? NewManagerEmployeeId, DateOnly EffectiveFrom);

/// <summary>Result of a career mutation: the affected record, whether anything actually changed (no event for a no-op) and the manager transition, if any.</summary>
public sealed record CareerOutcome(CareerRecord Record, bool Changed, ManagerTransition? ManagerTransition = null);

/// <summary>A placement change coming from a non-career write path (PUT /employees/{id}, bulk assign-manager, import).</summary>
public sealed record PlacementRequest(
    IReadOnlyList<OrgAssignmentInput> Assignments,
    Guid? ManagerEmployeeId,
    EmploymentType? EmploymentType,
    CareerChangeType ChangeType);

/// <summary>
/// One period of an employee's career (11.3.03 "Kariyer kaydı"): per-level org placement, manager, employment type
/// and why it started. Child entity of the <see cref="Employee"/> aggregate - it is never loaded or written on its
/// own, so every rule (one open record, no overlaps, ValidFrom ordering) stays inside the aggregate.
/// </summary>
public sealed class CareerRecord : TenantEntity<Guid>
{
    private readonly List<EmployeeOrgAssignment> _assignments = [];

    internal CareerRecord(Guid id, Guid tenantId, Guid employeeId, CareerRecordData data)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        ValidFrom = data.ValidFrom;
        ChangeType = data.ChangeType;
        EmploymentType = data.EmploymentType;
        ManagerEmployeeId = data.ManagerEmployeeId;
        Note = Trim(data.Note);
        foreach (var assignment in data.AssignmentList)
        {
            _assignments.Add(new EmployeeOrgAssignment(assignment.OrgUnitTypeId, assignment.OrgUnitId));
        }
    }

    private CareerRecord()
    {
    }

    public Guid EmployeeId { get; private set; }

    /// <summary>"Pozisyon başlama tarihi"; may differ from the hire date (e.g. taking the post after a 90-day training).</summary>
    public DateOnly ValidFrom { get; private set; }

    /// <summary>Null = still in effect; a later record closes this one at its ValidFrom - 1.</summary>
    public DateOnly? ValidTo { get; private set; }

    public CareerChangeType ChangeType { get; private set; }

    public EmploymentType? EmploymentType { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public string? Note { get; private set; }

    public IReadOnlyList<EmployeeOrgAssignment> Assignments => _assignments.AsReadOnly();

    public bool IsOpen => ValidTo is null;

    /// <summary>The unit of the deepest level this record assigns (highest OrgUnitType.SortOrder) - the denormalized <see cref="Employee.OrgUnitId"/>.</summary>
    public Guid? DeepestOrgUnitId(IReadOnlyDictionary<Guid, int> levelOrder) =>
        _assignments.Count == 0
            ? null
            : _assignments
                .OrderByDescending(a => levelOrder.TryGetValue(a.OrgUnitTypeId, out var order) ? order : int.MinValue)
                .ThenBy(a => a.OrgUnitTypeId)
                .First().OrgUnitId;

    public bool HasSameAssignments(IReadOnlyList<OrgAssignmentInput> assignments) =>
        _assignments.Count == assignments.Count &&
        assignments.All(a => _assignments.Any(existing => existing.OrgUnitTypeId == a.OrgUnitTypeId && existing.OrgUnitId == a.OrgUnitId));

    public IReadOnlyList<OrgAssignmentInput> AssignmentInputs() =>
        _assignments.Select(a => new OrgAssignmentInput(a.OrgUnitTypeId, a.OrgUnitId)).ToList();

    internal void Close(DateOnly validTo) => ValidTo = validTo;

    internal void Reopen(DateOnly? validTo) => ValidTo = validTo;

    internal void SetValidFrom(DateOnly validFrom) => ValidFrom = validFrom;

    /// <summary>Applies the editable content; returns true when something actually changed.</summary>
    internal bool Apply(CareerRecordData data)
    {
        var changed = false;
        if (ValidFrom != data.ValidFrom)
        {
            ValidFrom = data.ValidFrom;
            changed = true;
        }

        if (ChangeType != data.ChangeType)
        {
            ChangeType = data.ChangeType;
            changed = true;
        }

        if (EmploymentType != data.EmploymentType)
        {
            EmploymentType = data.EmploymentType;
            changed = true;
        }

        if (ManagerEmployeeId != data.ManagerEmployeeId)
        {
            ManagerEmployeeId = data.ManagerEmployeeId;
            changed = true;
        }

        var note = Trim(data.Note);
        if (!string.Equals(Note, note, StringComparison.Ordinal))
        {
            Note = note;
            changed = true;
        }

        return SetAssignments(data.AssignmentList) || changed;
    }

    private bool SetAssignments(IReadOnlyList<OrgAssignmentInput> assignments)
    {
        var changed = false;
        foreach (var existing in _assignments.ToList())
        {
            var incoming = assignments.FirstOrDefault(a => a.OrgUnitTypeId == existing.OrgUnitTypeId);
            if (incoming is null)
            {
                _assignments.Remove(existing);
                changed = true;
            }
            else if (incoming.OrgUnitId != existing.OrgUnitId)
            {
                existing.MoveTo(incoming.OrgUnitId);
                changed = true;
            }
        }

        foreach (var incoming in assignments.Where(a => _assignments.All(existing => existing.OrgUnitTypeId != a.OrgUnitTypeId)))
        {
            _assignments.Add(new EmployeeOrgAssignment(incoming.OrgUnitTypeId, incoming.OrgUnitId));
            changed = true;
        }

        return changed;
    }

    /// <summary>Content rules that do not need the rest of the aggregate (per-level uniqueness, note length, non-empty references).</summary>
    public static Result Validate(CareerRecordData data)
    {
        var assignments = data.AssignmentList;
        if (assignments.Any(a => a.OrgUnitTypeId == Guid.Empty || a.OrgUnitId == Guid.Empty))
        {
            return Error.Validation(EmployeeDomainErrors.CareerAssignmentInvalid);
        }

        if (assignments.Select(a => a.OrgUnitTypeId).Distinct().Count() != assignments.Count)
        {
            return Error.Validation(EmployeeDomainErrors.CareerDuplicateLevel);
        }

        if (Trim(data.Note) is { Length: > EmployeeLimits.CareerNoteMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.CareerNoteTooLong);
        }

        return Result.Success();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
