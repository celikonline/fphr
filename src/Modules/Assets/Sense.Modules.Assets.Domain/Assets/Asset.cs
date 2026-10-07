using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Domain.Assets;

public enum AssetStatus
{
    Unassigned = 0,
    Assigned = 1,
    Retired = 2,

    /// <summary>Assignment requested, waiting on the Workflow "AssetAssignment" approval (see RequestAssignment/CompleteAssignment).</summary>
    PendingAssignment = 3,

    /// <summary>Reported lost (while unassigned or while held by an employee); a terminal state, see MarkLost.</summary>
    Lost = 4,
}

/// <summary>Descriptive "künye" fields of an asset (all optional); null to Create/Update means "not provided".</summary>
public sealed record AssetDetails(
    string? AssetTag, string? Brand, string? Model, DateOnly? PurchaseDate, decimal? PurchaseCost, DateOnly? WarrantyEndsOn);

/// <summary>
/// Company equipment ("zimmet"): a category, serial number, description, künye details, and the current-assignment
/// slot (Status/AssignedEmployeeId/IssuedAt) backed by an assignment ledger (<see cref="Assignments"/>, v2). An employee
/// "currently has an open (unreturned) asset" is exactly Status == Assigned. TK-018: assignment no longer completes
/// directly - RequestAssignment moves Unassigned -> PendingAssignment and starts a Workflow approval;
/// CompleteAssignment/RejectAssignment (driven by ApprovalCompleted/ApprovalRejected) finish the transition.
/// The ledger is written only by this class's methods; repositories load it filtered to the open row
/// (<see cref="OpenAssignment"/>) for mutation, the full history is read separately.
/// </summary>
public sealed class Asset : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<AssetAssignment> _assignments = [];

    private Asset()
    {
    }

    private Asset(Guid id, Guid tenantId, Guid categoryId, string name, string? serialNumber, string? description)
        : base(id, tenantId)
    {
        CategoryId = categoryId;
        Name = name;
        SerialNumber = serialNumber;
        Description = description;
        Status = AssetStatus.Unassigned;
    }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? SerialNumber { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Demirbaş no; unique per tenant case-insensitively (enforced by the handlers via the repository).</summary>
    public string? AssetTag { get; private set; }

    public string? Brand { get; private set; }

    public string? Model { get; private set; }

    public DateOnly? PurchaseDate { get; private set; }

    public decimal? PurchaseCost { get; private set; }

    public DateOnly? WarrantyEndsOn { get; private set; }

    /// <summary>Current physical state; updated by Update and by Return (the condition reported at return).</summary>
    public AssetCondition Condition { get; private set; } = AssetCondition.Good;

    public AssetStatus Status { get; private set; }

    public Guid? AssignedEmployeeId { get; private set; }

    public DateOnly? IssuedAt { get; private set; }

    /// <summary>Date of the most recent return; kept for display after Return, cleared again once reassigned.</summary>
    public DateOnly? ReturnedAt { get; private set; }

    /// <summary>Set by RequestAssignment, cleared by CompleteAssignment/RejectAssignment; the Workflow approval process deciding the pending assignment.</summary>
    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>Requested assignee, valid only while Status == PendingAssignment.</summary>
    public Guid? PendingEmployeeId { get; private set; }

    /// <summary>Requested issue date, valid only while Status == PendingAssignment.</summary>
    public DateOnly? PendingIssuedAt { get; private set; }

    /// <summary>Assignment ledger; only the rows the repository loaded (mutation paths load just the open one).</summary>
    public IReadOnlyCollection<AssetAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>The not-yet-returned ledger row, present while Status == Assigned.</summary>
    public AssetAssignment? OpenAssignment => _assignments.FirstOrDefault(a => a.IsOpen);

    public static Result<Asset> Create(
        Guid tenantId, Guid categoryId, string name, string? serialNumber, string? description, AssetDetails? details = null)
    {
        var invalid = ValidateName(name) ?? ValidateDetails(details);
        if (invalid is not null)
        {
            return invalid;
        }

        var asset = new Asset(
            Guid.CreateVersion7(),
            tenantId,
            categoryId,
            name.Trim(),
            Trim(serialNumber, AssetsLimits.SerialNumberMaxLength),
            Trim(description, AssetsLimits.DescriptionMaxLength));
        asset.ApplyDetails(details);
        return asset;
    }

    /// <summary>Edits the descriptive fields; a null <paramref name="details"/>/<paramref name="condition"/> leaves the künye fields/condition untouched.</summary>
    public Result Update(string name, string? serialNumber, string? description, AssetDetails? details = null, AssetCondition? condition = null)
    {
        var invalid = ValidateName(name) ?? ValidateDetails(details);
        if (invalid is not null)
        {
            return invalid;
        }

        Name = Guard.MaxLength(name.Trim(), AssetsLimits.NameMaxLength);
        SerialNumber = Trim(serialNumber, AssetsLimits.SerialNumberMaxLength);
        Description = Trim(description, AssetsLimits.DescriptionMaxLength);
        ApplyDetails(details);
        Condition = condition ?? Condition;
        return Result.Success();
    }

    /// <summary>
    /// Unassigned/Retired -> Assigned, immediately (no approval). Kept for the bulk Excel import path
    /// (AssetImportRowHandler - a riskli_alan file, deliberately left untouched by TK-018) which records
    /// already-decided real-world assignments as data, not a live decision to route through Workflow. The
    /// interactive "zimmet ata" command uses RequestAssignment/CompleteAssignment below instead.
    /// </summary>
    public Result Assign(Guid employeeId, DateOnly issuedAt)
    {
        if (Status != AssetStatus.Unassigned)
        {
            return Error.Rule(AssetsDomainErrors.AlreadyAssigned);
        }

        OpenLedgerRow(employeeId, issuedAt);
        return Result.Success();
    }

    /// <summary>Unassigned -> PendingAssignment; the caller then starts a Workflow "AssetAssignment" approval and calls AttachApproval.</summary>
    public Result RequestAssignment(Guid employeeId, DateOnly issuedAt)
    {
        if (Status != AssetStatus.Unassigned)
        {
            return Error.Rule(AssetsDomainErrors.AlreadyAssigned);
        }

        Status = AssetStatus.PendingAssignment;
        PendingEmployeeId = employeeId;
        PendingIssuedAt = issuedAt;
        return Result.Success();
    }

    public void AttachApproval(Guid approvalRequestId) => ApprovalRequestId = approvalRequestId;

    /// <summary>Workflow approved the pending assignment (ApprovalCompleted) -> Assigned.</summary>
    public Result CompleteAssignment(Guid approvalRequestId, DateOnly at)
    {
        var mismatch = ValidatePendingApproval(approvalRequestId);
        if (mismatch is not null)
        {
            return mismatch;
        }

        OpenLedgerRow(PendingEmployeeId!.Value, PendingIssuedAt ?? at);
        PendingEmployeeId = null;
        PendingIssuedAt = null;
        ApprovalRequestId = null;
        return Result.Success();
    }

    /// <summary>Workflow rejected the pending assignment (ApprovalRejected) -> back to Unassigned.</summary>
    public Result RejectAssignment(Guid approvalRequestId)
    {
        var mismatch = ValidatePendingApproval(approvalRequestId);
        if (mismatch is not null)
        {
            return mismatch;
        }

        Status = AssetStatus.Unassigned;
        PendingEmployeeId = null;
        PendingIssuedAt = null;
        ApprovalRequestId = null;
        return Result.Success();
    }

    private Error? ValidatePendingApproval(Guid approvalRequestId) =>
        Status != AssetStatus.PendingAssignment || ApprovalRequestId != approvalRequestId
            ? Error.Conflict(AssetsDomainErrors.AssignmentApprovalMismatch)
            : null;

    /// <summary>
    /// Assigned -> Unassigned; closes the open ledger row with the returned condition (null keeps the asset's current
    /// condition, which the ledger row then records) and note, and keeps ReturnedAt as the "last returned" date for display.
    /// </summary>
    public Result Return(DateOnly returnedAt, AssetCondition? condition = null, string? note = null)
    {
        if (Status != AssetStatus.Assigned)
        {
            return Error.Rule(AssetsDomainErrors.NotAssigned);
        }

        if (IssuedAt is { } issuedAt && returnedAt < issuedAt)
        {
            return Error.Validation(AssetsDomainErrors.ReturnBeforeIssue);
        }

        if (ValidateNote(note) is { } noteError)
        {
            return noteError;
        }

        Condition = condition ?? Condition;
        OpenAssignment?.Close(returnedAt, AssignmentEndReason.Returned, Condition, TrimNote(note));
        Status = AssetStatus.Unassigned;
        AssignedEmployeeId = null;
        IssuedAt = null;
        ReturnedAt = returnedAt;
        return Result.Success();
    }

    /// <summary>
    /// Unassigned or Assigned -> Lost. When held by an employee the open ledger row closes with EndReason=Lost (no
    /// condition - there is nothing to inspect). Retired/Lost/PendingAssignment assets cannot be reported lost.
    /// </summary>
    public Result MarkLost(DateOnly occurredOn, string? note = null)
    {
        if (Status is not (AssetStatus.Unassigned or AssetStatus.Assigned))
        {
            return Error.Rule(AssetsDomainErrors.CannotLose);
        }

        if (IssuedAt is { } issuedAt && occurredOn < issuedAt)
        {
            return Error.Validation(AssetsDomainErrors.ReturnBeforeIssue);
        }

        if (ValidateNote(note) is { } noteError)
        {
            return noteError;
        }

        OpenAssignment?.Close(occurredOn, AssignmentEndReason.Lost, null, TrimNote(note));
        Status = AssetStatus.Lost;
        AssignedEmployeeId = null;
        IssuedAt = null;
        return Result.Success();
    }

    /// <summary>The assignee confirms receipt ("teslim aldım"): only the employee holding the open assignment, once.</summary>
    public Result Acknowledge(Guid employeeId, DateTime at)
    {
        if (Status != AssetStatus.Assigned || OpenAssignment is not { } open)
        {
            return Error.Rule(AssetsDomainErrors.NotAssigned);
        }

        if (open.EmployeeId != employeeId)
        {
            return Error.Forbidden(AssetsDomainErrors.NotAssignee);
        }

        if (open.AcknowledgedAt is not null)
        {
            return Error.Rule(AssetsDomainErrors.AlreadyAcknowledged);
        }

        open.Acknowledge(at);
        return Result.Success();
    }

    /// <summary>Unassigned -> Retired. Must Return first if currently Assigned; cannot retire while an assignment approval is pending.</summary>
    public Result Retire()
    {
        if (Status is AssetStatus.Assigned or AssetStatus.PendingAssignment)
        {
            return Error.Rule(AssetsDomainErrors.CannotRetireWhileAssigned);
        }

        Status = AssetStatus.Retired;
        return Result.Success();
    }

    /// <summary>The single place that turns the asset Assigned: sets the current-assignment slot and opens the ledger row.</summary>
    private void OpenLedgerRow(Guid employeeId, DateOnly issuedAt)
    {
        Status = AssetStatus.Assigned;
        AssignedEmployeeId = employeeId;
        IssuedAt = issuedAt;
        ReturnedAt = null;
        _assignments.Add(AssetAssignment.Open(Id, TenantId, employeeId, issuedAt));
    }

    private void ApplyDetails(AssetDetails? details)
    {
        if (details is null)
        {
            return;
        }

        AssetTag = Trim(details.AssetTag, AssetsLimits.AssetTagMaxLength);
        Brand = Trim(details.Brand, AssetsLimits.BrandMaxLength);
        Model = Trim(details.Model, AssetsLimits.ModelMaxLength);
        PurchaseDate = details.PurchaseDate;
        PurchaseCost = details.PurchaseCost;
        WarrantyEndsOn = details.WarrantyEndsOn;
    }

    private static Error? ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name) ? Error.Validation(AssetsDomainErrors.NameRequired) : null;

    private static Error? ValidateDetails(AssetDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        if (details.PurchaseCost is < 0)
        {
            return Error.Validation(AssetsDomainErrors.CostInvalid);
        }

        return details is { PurchaseDate: { } purchased, WarrantyEndsOn: { } warrantyEnds } && warrantyEnds < purchased
            ? Error.Validation(AssetsDomainErrors.WarrantyBeforePurchase)
            : null;
    }

    private static Error? ValidateNote(string? note) =>
        note?.Trim().Length > AssetsLimits.NoteMaxLength ? Error.Validation(AssetsDomainErrors.NoteTooLong) : null;

    private static string? TrimNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string? Trim(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}
