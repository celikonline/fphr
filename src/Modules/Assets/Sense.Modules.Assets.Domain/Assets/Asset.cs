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
}

/// <summary>
/// Company equipment ("zimmet"): a category, serial number, description, and a single current-assignment slot
/// (no assignment history/ledger - matches the roadmap's simple "zimmet şablonu" scope, item #65). An employee
/// "currently has an open (unreturned) asset" is exactly Status == Assigned. TK-018: assignment no longer completes
/// directly - RequestAssignment moves Unassigned -> PendingAssignment and starts a Workflow approval;
/// CompleteAssignment/RejectAssignment (driven by ApprovalCompleted/ApprovalRejected) finish the transition.
/// </summary>
public sealed class Asset : TenantAggregateRoot<Guid>, IAuditLogged
{
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

    public static Result<Asset> Create(Guid tenantId, Guid categoryId, string name, string? serialNumber, string? description)
    {
        var validated = ValidateName(name);
        if (validated is not null)
        {
            return validated;
        }

        return new Asset(
            Guid.CreateVersion7(),
            tenantId,
            categoryId,
            name.Trim(),
            Trim(serialNumber, AssetsLimits.SerialNumberMaxLength),
            Trim(description, AssetsLimits.DescriptionMaxLength));
    }

    public Result Update(string name, string? serialNumber, string? description)
    {
        var validated = ValidateName(name);
        if (validated is not null)
        {
            return validated;
        }

        Name = Guard.MaxLength(name.Trim(), AssetsLimits.NameMaxLength);
        SerialNumber = Trim(serialNumber, AssetsLimits.SerialNumberMaxLength);
        Description = Trim(description, AssetsLimits.DescriptionMaxLength);
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

        Status = AssetStatus.Assigned;
        AssignedEmployeeId = employeeId;
        IssuedAt = issuedAt;
        ReturnedAt = null;
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

        Status = AssetStatus.Assigned;
        AssignedEmployeeId = PendingEmployeeId;
        IssuedAt = PendingIssuedAt ?? at;
        ReturnedAt = null;
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

    /// <summary>Assigned -> Unassigned; clears the current assignment, keeps ReturnedAt as the "last returned" date for display.</summary>
    public Result Return(DateOnly returnedAt)
    {
        if (Status != AssetStatus.Assigned)
        {
            return Error.Rule(AssetsDomainErrors.NotAssigned);
        }

        if (IssuedAt is { } issuedAt && returnedAt < issuedAt)
        {
            return Error.Validation(AssetsDomainErrors.ReturnBeforeIssue);
        }

        Status = AssetStatus.Unassigned;
        AssignedEmployeeId = null;
        IssuedAt = null;
        ReturnedAt = returnedAt;
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

    private static Error? ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name) ? Error.Validation(AssetsDomainErrors.NameRequired) : null;

    private static string? Trim(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}
