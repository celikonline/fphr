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

    /// <summary>Serbest zaman m.41: 6 ay (Overtime.CompTimeExpiryMonths) içinde kullandırılmadı (kart senseik-012 §3).</summary>
    Expired = 2,
}

/// <summary>
/// Conversion of an approved overtime request (11.3.5 "OvertimeConversion"). TimeOff (<see cref="ToTimeOff"/>) posts an
/// ExtraGrant ledger row (<see cref="LedgerEntryId"/>). Pay (<see cref="ToPay"/>, kart senseik-030) records the explicit
/// "nakde çevir" decision without touching the ledger - <c>Leave.Contracts.IOvertimePayrollReader</c> already treats a
/// request as cash-payable unless its conversion is a Posted TimeOff (never-converted and Kind=Pay land in the same
/// Rate25/Rate50 bucket there), so this factory exists for the audit trail and to block a later TimeOff conversion of
/// the same request via <see cref="OvertimeRequest.MarkConverted"/>. Reversal still comes later.
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

    /// <summary>Yalnız <see cref="OvertimeConversionKind.TimeOff"/> için anlamlı: bu tarihe kadar kullandırılmalı (m.41, 6 ay varsayılan - <c>Overtime.CompTimeExpiryMonths</c>), aksi halde <see cref="ExpireIfPastDue"/> ile <see cref="OvertimeConversionStatus.Expired"/> olur.</summary>
    public DateOnly? UsedByDate { get; private set; }

    public OvertimeConversionStatus Status { get; private set; }

    public Guid? ConvertedByUserId { get; private set; }

    public DateTime ConvertedAt { get; private set; }

    public string? Note { get; private set; }

    public static Result<OvertimeConversion> ToTimeOff(
        OvertimeRequest request, OvertimeMinutesBasis basis, decimal rate, int dailyWorkMinutes, Guid leaveTypeId, Guid? convertedByUserId, DateTime at,
        string? note, DateOnly? usedByDate = null)
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
            UsedByDate = usedByDate,
        };
    }

    /// <summary>
    /// "Fazla mesaiyi ödemeye çevir" (kart senseik-030): approved, not-yet-converted request only. No rate/ledger math -
    /// <see cref="RateSnapshot"/> is the request's own <see cref="OvertimeRequest.PayRateMultiplier"/> snapshot (%25/%50,
    /// kart senseik-012 §1) for the audit trail; the cash amount is computed by Payroll from its own wage data when it
    /// reads <c>IOvertimePayrollReader</c> (Leave has no salary access - Modules_TalkOnlyThroughContracts).
    /// </summary>
    public static Result<OvertimeConversion> ToPay(OvertimeRequest request, OvertimeMinutesBasis basis, Guid? convertedByUserId, DateTime at, string? note)
    {
        if (request.Status != OvertimeRequestStatus.Approved)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeNotApproved);
        }

        if (request.ConversionId is not null)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeAlreadyConverted);
        }

        var basisMinutes = basis == OvertimeMinutesBasis.Full ? request.DurationMinutes : request.PayableMinutes;
        if (basisMinutes <= 0)
        {
            return Error.Validation(LeaveDomainErrors.OvertimeNothingToConvert);
        }

        return new OvertimeConversion(request.TenantId, request.Id, request.EmployeeId)
        {
            Kind = OvertimeConversionKind.Pay,
            MinutesBasis = basis,
            RateSnapshot = request.PayRateMultiplier,
            BasisMinutes = basisMinutes,
            ResultMinutes = basisMinutes,
            ResultDays = 0m,
            DailyWorkMinutesSnapshot = 0,
            LeaveTypeId = null,
            Status = OvertimeConversionStatus.Posted,
            ConvertedByUserId = convertedByUserId,
            ConvertedAt = at,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            UsedByDate = null,
        };
    }

    public void AttachLedgerEntry(Guid ledgerEntryId) => LedgerEntryId = ledgerEntryId;

    /// <summary>
    /// m.41 6 ay kuralı (kart senseik-012 §3): <see cref="UsedByDate"/> geçtiyse ve hâlâ Posted ise Expired'a döner -
    /// aynı <c>Compensatory.CompensatoryWorkLedger.ExpireIfPastDue</c> deseni (query-time/lazy expiry, arka plan
    /// işi yok). Yalnız TimeOff dönüşümleri için anlamlıdır (Pay dönüşümlerinde UsedByDate hep null).
    /// </summary>
    public void ExpireIfPastDue(DateOnly today)
    {
        if (Status == OvertimeConversionStatus.Posted && UsedByDate is { } dueDate && today > dueDate)
        {
            Status = OvertimeConversionStatus.Expired;
        }
    }
}
