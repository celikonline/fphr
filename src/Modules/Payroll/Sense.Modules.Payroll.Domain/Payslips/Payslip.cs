using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>
/// One employee's payroll line for one <see cref="Periods.PayrollPeriod"/> (11.3.7). This slice only stores the
/// fields the calculation trace needs to reference (gross/net/employer cost + the input snapshot/hash the "Değişti"
/// mechanism will compare against in P3) - not a full aggregate with line items, dirty flags or approval state yet.
/// </summary>
public sealed class Payslip : TenantAggregateRoot<Guid>
{
    private readonly List<PayslipAcknowledgementRecord> _acknowledgementHistory = [];

    private Payslip()
    {
    }

    private Payslip(
        Guid id, Guid tenantId, Guid employeeId, Guid payrollPeriodId, string inputSnapshot, string inputHash,
        int version, decimal grossAmount, decimal netAmount, decimal employerCost, decimal cumulativeGvBase, Guid? costCenterId, bool costCenterSnapshotCaptured)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        PayrollPeriodId = payrollPeriodId;
        InputSnapshot = inputSnapshot;
        InputHash = inputHash;
        Version = version;
        GrossAmount = grossAmount;
        NetAmount = netAmount;
        EmployerCost = employerCost;
        CumulativeGvBase = cumulativeGvBase;
        CostCenterId = costCenterId;
        CostCenterSnapshotCaptured = costCenterSnapshotCaptured;
    }

    public Guid EmployeeId { get; private set; }

    public Guid PayrollPeriodId { get; private set; }

    /// <summary>JSON snapshot of the inputs the calculation used (11.3.7 "Değişti" mechanism, InputHash compares this later).</summary>
    public string InputSnapshot { get; private set; } = string.Empty;

    public string InputHash { get; private set; } = string.Empty;

    /// <summary>Incremented on each recalculation; <see cref="Payslips.PayslipCalculationTrace.Version"/> mirrors it.</summary>
    public int Version { get; private set; }

    public decimal GrossAmount { get; private set; }

    public decimal NetAmount { get; private set; }

    public decimal EmployerCost { get; private set; }

    /// <summary>senseik-019 2a: bu ayın CUMULATIVE_BASE trace çıktısı (yıl başından bu aya kadarki kümülatif GV
    /// matrahı) - bir sonraki ayın PriorCumulativeGvBase'i olarak PayrollPeriodCalculationService tarafından okunur
    /// (aynı takvim yılı içindeyse; Ocak'ta veya önceki ay hiç hesaplanmadıysa 0'dan başlanır).</summary>
    public decimal CumulativeGvBase { get; private set; }

    /// <summary>Resolved cost center at the time this payslip version was calculated; null can mean unassigned.</summary>
    public Guid? CostCenterId { get; private set; }

    /// <summary>Distinguishes a captured null assignment from legacy payslips created before this snapshot existed.</summary>
    public bool CostCenterSnapshotCaptured { get; private set; }

    /// <summary>When the bank's dekont (receipt) confirmed this payment matched by IBAN and amount; null until matched. A
    /// recalculation invalidates it, since the figures it was matched against no longer hold.</summary>
    public DateTimeOffset? PaymentConfirmedAt { get; private set; }

    public static Payslip CreateFirstVersion(
        Guid tenantId, Guid employeeId, Guid payrollPeriodId, string inputSnapshot, string inputHash,
        decimal grossAmount, decimal netAmount, decimal employerCost, decimal cumulativeGvBase = 0m,
        Guid? costCenterId = null, bool costCenterSnapshotCaptured = false)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);
        Guard.NotDefault(payrollPeriodId);
        return new Payslip(
            Guid.NewGuid(), tenantId, employeeId, payrollPeriodId, Guard.NotEmpty(inputSnapshot), Guard.NotEmpty(inputHash),
            version: 1, grossAmount, netAmount, employerCost, cumulativeGvBase, costCenterId, costCenterSnapshotCaptured);
    }

    /// <summary>Recompute with new figures (P3 "Değişenleri yeniden hesapla"): bumps <see cref="Version"/>.</summary>
    public void Recalculate(string inputSnapshot, string inputHash, decimal grossAmount, decimal netAmount, decimal employerCost, decimal cumulativeGvBase = 0m,
        Guid? costCenterId = null, bool costCenterSnapshotCaptured = false)
    {
        InputSnapshot = Guard.NotEmpty(inputSnapshot);
        InputHash = Guard.NotEmpty(inputHash);
        GrossAmount = grossAmount;
        NetAmount = netAmount;
        EmployerCost = employerCost;
        CumulativeGvBase = cumulativeGvBase;
        CostCenterId = costCenterId;
        CostCenterSnapshotCaptured = costCenterSnapshotCaptured;
        PaymentConfirmedAt = null;
        ResetPublication();
        Version++;
    }

    /// <summary>Records that a bank dekont matched this payslip's IBAN and net amount for this version.</summary>
    public void ConfirmPayment(DateTimeOffset at) => PaymentConfirmedAt = at;

    /// <summary>
    /// When the period was marked paid and this payslip was opened to the employee (E2, 4857 md.37 tebliğ); null until
    /// then. Idempotent: a period paid again after a reopen keeps the first publication of an unchanged payslip.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>K4 "imza": server time the employee acknowledged this version of the payslip; null until then.</summary>
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    /// <summary>K4: client IP the acknowledgement came from (as the API saw it).</summary>
    public string? AcknowledgedFromIp { get; private set; }

    /// <summary>K4: client (User-Agent) the acknowledgement came from, truncated to <see cref="PayrollLimits.UserAgentMaxLength"/>.</summary>
    public string? AcknowledgedUserAgent { get; private set; }

    public PayslipObjectionStatus ObjectionStatus { get; private set; }

    /// <summary>The employee's stated reason for objecting to this payslip (<see cref="ObjectionStatus"/> != None).</summary>
    public string? ObjectionReason { get; private set; }

    public DateTimeOffset? ObjectedAt { get; private set; }

    /// <summary>The Workflow "PayslipObjection" approval request deciding the objection (approve = objection accepted).</summary>
    public Guid? ObjectionApprovalRequestId { get; private set; }

    public DateTimeOffset? ObjectionDecidedAt { get; private set; }

    /// <summary>
    /// Append-only K4 evidence of earlier versions: each recalculation of an acknowledged payslip (or one with a decided
    /// objection) appends that version's acknowledgement/objection here before the live fields are reset.
    /// </summary>
    public IReadOnlyList<PayslipAcknowledgementRecord> AcknowledgementHistory => _acknowledgementHistory;

    /// <summary>Acknowledgement still expected: published, not acknowledged, no objection pending or accepted.</summary>
    public bool AwaitsAcknowledgement =>
        PublishedAt is not null && AcknowledgedAt is null
        && ObjectionStatus is PayslipObjectionStatus.None or PayslipObjectionStatus.Rejected;

    /// <summary>Opens the payslip to the employee (period marked paid). Keeps an earlier publication.</summary>
    public void Publish(DateTimeOffset at) => PublishedAt ??= at;

    /// <summary>
    /// K4: the employee acknowledges the published payslip once - server timestamp + IP + client are kept as evidence.
    /// Refused before publication, a second time, and while an objection is pending.
    /// </summary>
    public Result Acknowledge(DateTimeOffset at, string? ipAddress, string? userAgent)
    {
        if (PublishedAt is null)
        {
            return Error.Rule(PayrollDomainErrors.PayslipNotPublished);
        }

        if (AcknowledgedAt is not null)
        {
            return Error.Conflict(PayrollDomainErrors.PayslipAlreadyAcknowledged);
        }

        if (ObjectionStatus == PayslipObjectionStatus.Pending)
        {
            return Error.Conflict(PayrollDomainErrors.PayslipObjectionPending);
        }

        AcknowledgedAt = at;
        AcknowledgedFromIp = Truncate(ipAddress, PayrollLimits.ClientIpMaxLength);
        AcknowledgedUserAgent = Truncate(userAgent, PayrollLimits.UserAgentMaxLength);
        return Result.Success();
    }

    /// <summary>The employee objects to the published, not yet acknowledged payslip (one open objection per version).</summary>
    public Result FileObjection(string? reason, DateTimeOffset at)
    {
        if (PublishedAt is null)
        {
            return Error.Rule(PayrollDomainErrors.PayslipNotPublished);
        }

        if (AcknowledgedAt is not null)
        {
            return Error.Conflict(PayrollDomainErrors.PayslipAlreadyAcknowledged);
        }

        if (ObjectionStatus != PayslipObjectionStatus.None)
        {
            return Error.Conflict(PayrollDomainErrors.PayslipObjectionAlreadyFiled);
        }

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > PayrollLimits.ObjectionReasonMaxLength)
        {
            return Error.Validation(PayrollDomainErrors.PayslipObjectionReasonInvalid);
        }

        ObjectionStatus = PayslipObjectionStatus.Pending;
        ObjectionReason = trimmed;
        ObjectedAt = at;
        ObjectionApprovalRequestId = null;
        ObjectionDecidedAt = null;
        return Result.Success();
    }

    /// <summary>Links the pending objection to the Workflow approval request started for it.</summary>
    public void AttachObjectionApproval(Guid approvalRequestId) => ObjectionApprovalRequestId = Guard.NotDefault(approvalRequestId);

    /// <summary>Undoes <see cref="FileObjection"/> when its Workflow approval could not be started (no request attached yet).</summary>
    public void WithdrawUnsubmittedObjection()
    {
        if (ObjectionStatus != PayslipObjectionStatus.Pending || ObjectionApprovalRequestId is not null)
        {
            return;
        }

        ClearObjection();
    }

    /// <summary>Workflow approved the objection's request: the objection is accepted (payroll has to correct the payslip).</summary>
    public Result AcceptObjection(Guid approvalRequestId, DateTimeOffset at) => DecideObjection(approvalRequestId, PayslipObjectionStatus.Accepted, at);

    /// <summary>Workflow rejected the objection's request: the payslip stands and can still be acknowledged.</summary>
    public Result RejectObjection(Guid approvalRequestId, DateTimeOffset at) => DecideObjection(approvalRequestId, PayslipObjectionStatus.Rejected, at);

    private Result DecideObjection(Guid approvalRequestId, PayslipObjectionStatus outcome, DateTimeOffset at)
    {
        if (ObjectionStatus != PayslipObjectionStatus.Pending || ObjectionApprovalRequestId != approvalRequestId)
        {
            return Error.Conflict(PayrollDomainErrors.PayslipObjectionNotPending);
        }

        ObjectionStatus = outcome;
        ObjectionDecidedAt = at;
        return Result.Success();
    }

    /// <summary>
    /// A recalculation changes the figures the employee saw: the new version must be published and acknowledged again.
    /// A decided objection is cleared so the new version can be objected to; a pending one keeps running in Workflow.
    /// </summary>
    private void ResetPublication()
    {
        ArchiveAcknowledgement();
        PublishedAt = null;
        AcknowledgedAt = null;
        AcknowledgedFromIp = null;
        AcknowledgedUserAgent = null;
        if (ObjectionStatus is PayslipObjectionStatus.Accepted or PayslipObjectionStatus.Rejected)
        {
            ClearObjection();
        }
    }

    /// <summary>
    /// Keeps the evidence the reset is about to clear: an acknowledgement and/or a decided objection of the current
    /// version. A pending objection is not archived - it stays live and keeps running in Workflow.
    /// </summary>
    private void ArchiveAcknowledgement()
    {
        var decidedObjection = ObjectionStatus is PayslipObjectionStatus.Accepted or PayslipObjectionStatus.Rejected;
        if (AcknowledgedAt is null && !decidedObjection)
        {
            return;
        }

        _acknowledgementHistory.Add(new PayslipAcknowledgementRecord(
            Version, PublishedAt, AcknowledgedAt, AcknowledgedFromIp, AcknowledgedUserAgent,
            decidedObjection ? ObjectionStatus : null,
            decidedObjection ? ObjectionReason : null,
            decidedObjection ? ObjectedAt : null,
            decidedObjection ? ObjectionApprovalRequestId : null,
            decidedObjection ? ObjectionDecidedAt : null));
    }

    private void ClearObjection()
    {
        ObjectionStatus = PayslipObjectionStatus.None;
        ObjectionReason = null;
        ObjectedAt = null;
        ObjectionApprovalRequestId = null;
        ObjectionDecidedAt = null;
    }

    private static string? Truncate(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
