using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Garnishments;

/// <summary>
/// Bir çalışan için açılmış icra/nafaka kesinti emri (senseik-019 2c, İİK m.83). Sabit bir aylık taksit tutarı
/// (<see cref="MonthlyInstallmentAmount"/>) ile açılır ve her bordro döneminde
/// <see cref="Calculation.GarnishmentCalculator"/>'ın uyguladığı tutar kadar <see cref="RemainingBalance"/>'dan
/// düşülür; bakiye tükenince kendiliğinden kapanır. Cross-module referans (EmployeeId) taşıdığı için kendi başına
/// bir aggregate'tir (EmployeePayrollProfile ile aynı desen).
/// </summary>
public sealed class GarnishmentOrder : TenantAggregateRoot<Guid>
{
    private GarnishmentOrder()
    {
    }

    private GarnishmentOrder(
        Guid id, Guid tenantId, Guid employeeId, GarnishmentType type, string caseReference,
        decimal monthlyInstallmentAmount, decimal remainingBalance, DateOnly startDate)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Type = type;
        CaseReference = caseReference;
        MonthlyInstallmentAmount = monthlyInstallmentAmount;
        RemainingBalance = remainingBalance;
        StartDate = startDate;
    }

    public Guid EmployeeId { get; private set; }

    public GarnishmentType Type { get; private set; }

    /// <summary>İcra dosya numarası / nafaka karar referansı (serbest metin, denetim amaçlı).</summary>
    public string CaseReference { get; private set; } = string.Empty;

    public decimal MonthlyInstallmentAmount { get; private set; }

    /// <summary>Kalan toplam borç; her ay uygulanan kesinti kadar azalır, sıfırlanınca emir kapanır.</summary>
    public decimal RemainingBalance { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? ClosedDate { get; private set; }

    public bool IsClosed => ClosedDate is not null;

    public static Result<GarnishmentOrder> Create(
        Guid tenantId, Guid employeeId, GarnishmentType type, string caseReference,
        decimal monthlyInstallmentAmount, decimal remainingBalance, DateOnly startDate)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(employeeId);

        if (string.IsNullOrWhiteSpace(caseReference))
        {
            return Error.Validation(PayrollDomainErrors.GarnishmentCaseReferenceRequired);
        }

        if (monthlyInstallmentAmount <= 0m)
        {
            return Error.Validation(PayrollDomainErrors.GarnishmentInstallmentInvalid);
        }

        if (remainingBalance <= 0m)
        {
            return Error.Validation(PayrollDomainErrors.GarnishmentBalanceInvalid);
        }

        return new GarnishmentOrder(Guid.NewGuid(), tenantId, employeeId, type, caseReference.Trim(), monthlyInstallmentAmount, remainingBalance, startDate);
    }

    /// <summary>Bu ayki uygulanan kesinti tutarını bakiyeden düşer (GarnishmentCalculator'ın sonucu); bakiye sıfıra
    /// inince <paramref name="asOf"/> tarihiyle otomatik kapanır. Zaten kapalı bir emre çağrılmaz (Application katmanı
    /// yalnızca aktif emirleri hesaba katar).</summary>
    public void ApplyMonthlyDeduction(decimal amount, DateOnly asOf)
    {
        if (amount <= 0m || IsClosed)
        {
            return;
        }

        RemainingBalance = Math.Max(0m, decimal.Round(RemainingBalance - amount, 2, MidpointRounding.AwayFromZero));
        if (RemainingBalance <= 0m)
        {
            ClosedDate = asOf;
        }
    }

    /// <summary>Manuel kapama (örn. borç başka yolla kapandı, mahkeme kararı kalktı).</summary>
    public Result Close(DateOnly closedDate)
    {
        if (IsClosed)
        {
            return Error.Validation(PayrollDomainErrors.GarnishmentAlreadyClosed);
        }

        if (closedDate < StartDate)
        {
            return Error.Validation(PayrollDomainErrors.GarnishmentDateRangeInvalid);
        }

        ClosedDate = closedDate;
        return Result.Success();
    }
}
