using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Assets.Domain.Assets;

/// <summary>Physical state of an item: the asset's current one, and the one recorded at return.</summary>
public enum AssetCondition
{
    Good = 0,
    Fair = 1,
    Damaged = 2,
}

/// <summary>Why a ledger row was closed.</summary>
public enum AssignmentEndReason
{
    Returned = 0,
    Lost = 1,
}

/// <summary>
/// One row of an asset's assignment ledger ("zimmet defteri"): who held it, from when to when, how it ended and
/// whether the employee acknowledged receipt. Open while ReturnedAt is null - an asset has at most one open row
/// (partial unique index). Rows are only written through Asset's methods (Assign/CompleteAssignment open one,
/// Return/MarkLost close it, Acknowledge stamps it), which is also how the Excel import fills the ledger.
/// </summary>
public sealed class AssetAssignment : TenantEntity<Guid>
{
    private AssetAssignment()
    {
    }

    private AssetAssignment(Guid id, Guid tenantId, Guid assetId, Guid employeeId, DateOnly issuedAt)
        : base(id, tenantId)
    {
        AssetId = assetId;
        EmployeeId = employeeId;
        IssuedAt = issuedAt;
    }

    public Guid AssetId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateOnly IssuedAt { get; private set; }

    public DateOnly? ReturnedAt { get; private set; }

    public AssignmentEndReason? EndReason { get; private set; }

    /// <summary>Condition reported at return; null for a loss (no item to inspect) and for open rows.</summary>
    public AssetCondition? ReturnCondition { get; private set; }

    public string? Note { get; private set; }

    /// <summary>UTC instant the employee confirmed receipt ("teslim aldım"); null until then.</summary>
    public DateTime? AcknowledgedAt { get; private set; }

    public bool IsOpen => ReturnedAt is null;

    internal static AssetAssignment Open(Guid assetId, Guid tenantId, Guid employeeId, DateOnly issuedAt) =>
        new(Guid.CreateVersion7(), tenantId, assetId, employeeId, issuedAt);

    internal void Close(DateOnly returnedAt, AssignmentEndReason reason, AssetCondition? condition, string? note)
    {
        ReturnedAt = returnedAt;
        EndReason = reason;
        ReturnCondition = condition;
        Note = note;
    }

    internal void Acknowledge(DateTime at) => AcknowledgedAt = at;
}
