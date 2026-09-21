using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Payroll.Domain.ExitSettlements;

/// <summary>
/// "Çıkış hesabı": the kıdem/ihbar/kullanılmamış izin ücreti amounts frozen at the moment an employee was terminated,
/// with the step trace that produced them. Unlike the wizard's read-only estimate this is a record - later salary,
/// legislation or leave-balance changes never move it. Own aggregate because it carries the cross-module
/// <see cref="EmployeeId"/> (same pattern as <c>GarnishmentOrder</c>).
/// </summary>
public sealed class ExitSettlement : TenantAggregateRoot<Guid>
{
    private ExitSettlement()
    {
    }

    private ExitSettlement(Guid id, Guid tenantId, ExitSettlementData data, DateTimeOffset calculatedAt)
        : base(id, tenantId)
    {
        EmployeeId = data.EmployeeId;
        TerminationDate = data.TerminationDate;
        CauseCategory = data.CauseCategory;
        Currency = data.Currency;
        SeniorityDays = data.SeniorityDays;
        DailyGrossWage = data.DailyGrossWage;
        SeveranceAmount = data.SeveranceAmount;
        NoticeAmount = data.NoticeAmount;
        UnusedLeaveAmount = data.UnusedLeaveAmount;
        TotalAmount = data.TotalAmount;
        StepsJson = data.StepsJson;
        CalculatedAt = calculatedAt;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly TerminationDate { get; private set; }

    public SeveranceCauseCategory CauseCategory { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public int SeniorityDays { get; private set; }

    public decimal DailyGrossWage { get; private set; }

    public decimal SeveranceAmount { get; private set; }

    public decimal NoticeAmount { get; private set; }

    public decimal UnusedLeaveAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>The calculator's trace steps as JSON (audit document, same purpose as a payslip's calculation trace).</summary>
    public string StepsJson { get; private set; } = "[]";

    public DateTimeOffset CalculatedAt { get; private set; }

    public static ExitSettlement Create(Guid tenantId, ExitSettlementData data, DateTimeOffset calculatedAt)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(data.EmployeeId);
        Guard.Against(data.TotalAmount < 0m, "TotalAmount must not be negative.");
        return new ExitSettlement(Guid.NewGuid(), tenantId, data, calculatedAt);
    }
}

/// <summary>Plain values a settlement is created from (keeps the factory signature short and the entity immutable).</summary>
public sealed record ExitSettlementData(
    Guid EmployeeId,
    DateOnly TerminationDate,
    SeveranceCauseCategory CauseCategory,
    string Currency,
    int SeniorityDays,
    decimal DailyGrossWage,
    decimal SeveranceAmount,
    decimal NoticeAmount,
    decimal UnusedLeaveAmount,
    decimal TotalAmount,
    string StepsJson);
