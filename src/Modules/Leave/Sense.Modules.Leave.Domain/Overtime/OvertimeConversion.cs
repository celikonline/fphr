using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.Overtime;

public enum OvertimeConversionKind
{
    Pay = 0,
    TimeOff = 1,
}

/// <summary>Which duration a conversion is based on: PayableMinutes (default) or the full DurationMinutes (leave.overtimes.override_payable).</summary>
public enum OvertimeMinutesBasis
{
    Payable = 0,
    Full = 1,
}

public enum OvertimeConversionStatus
{
    Posted = 0,
    Reversed = 1,
}

/// <summary>
/// Conversion of an approved overtime request (11.3.5 "OvertimeConversion"). This slice implements TimeOff: the result is an
/// ExtraGrant ledger row (<see cref="LedgerEntryId"/>). Pay (Expense PayrollAdjustment) and reversal come later.
/// </summary>
public sealed class OvertimeConversion : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OvertimeConversion()
    {
    }

    private OvertimeConversion(Guid tenantId, Guid overtimeRequestId, Guid employeeId)
        : base(Guid.CreateVersion7(), tenantId)
    {
        OvertimeRequestId = overtimeRequestId;
        EmployeeId = employeeId;
    }

    public Guid OvertimeRequestId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public OvertimeConversionKind Kind { get; private set; }

    public OvertimeMinutesBasis MinutesBasis { get; private set; }

    public decimal RateSnapshot { get; private set; }

    public int BasisMinutes { get; private set; }

    public int ResultMinutes { get; private set; }

    public decimal ResultDays { get; private set; }

    public int DailyWorkMinutesSnapshot { get; private set; }

    public Guid? LeaveTypeId { get; private set; }

    public Guid? LedgerEntryId { get; private set; }

    public OvertimeConversionStatus Status { get; private set; }

    public Guid? ConvertedByUserId { get; private set; }

    public DateTime ConvertedAt { get; private set; }

    public string? Note { get; private set; }

    public static Result<OvertimeConversion> ToTimeOff(
        OvertimeRequest request, OvertimeMinutesBasis basis, decimal rate, int dailyWorkMinutes, Guid leaveTypeId, Guid? convertedByUserId, DateTime at, string? note)
    {
        if (request.Status != OvertimeRequestStatus.Approved)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeNotApproved);
        }

        if (request.ConversionId is not null)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeAlreadyConverted);
        }

        if (rate < OvertimeLimits.MinRate || rate > OvertimeLimits.MaxRate || dailyWorkMinutes <= 0)
        {
            return Error.Validation(LeaveDomainErrors.OvertimeInvalidRate, ("min", OvertimeLimits.MinRate), ("max", OvertimeLimits.MaxRate));
        }

        var basisMinutes = basis == OvertimeMinutesBasis.Full ? request.DurationMinutes : request.PayableMinutes;
        var (resultMinutes, resultDays) = OvertimeMath.TimeOff(basisMinutes, rate, dailyWorkMinutes);
        if (resultMinutes <= 0)
        {
            return Error.Validation(LeaveDomainErrors.OvertimeNothingToConvert);
        }

        return new OvertimeConversion(request.TenantId, request.Id, request.EmployeeId)
        {
            Kind = OvertimeConversionKind.TimeOff,
            MinutesBasis = basis,
            RateSnapshot = rate,
            BasisMinutes = basisMinutes,
            ResultMinutes = resultMinutes,
            ResultDays = resultDays,
            DailyWorkMinutesSnapshot = dailyWorkMinutes,
            LeaveTypeId = leaveTypeId,
            Status = OvertimeConversionStatus.Posted,
            ConvertedByUserId = convertedByUserId,
            ConvertedAt = at,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }

    public void AttachLedgerEntry(Guid ledgerEntryId) => LedgerEntryId = ledgerEntryId;
}
