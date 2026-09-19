namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>One active <see cref="Garnishments.GarnishmentOrder"/>'s figures as the calculator needs them (senseik-019
/// 2c). <see cref="MonthlyInstallmentAmount"/> is the fixed taksit tutarı; <see cref="RemainingBalance"/> caps the
/// last installment so the order does not over-collect.</summary>
public sealed record GarnishmentDeductionInput(Guid OrderId, GarnishmentType Type, decimal MonthlyInstallmentAmount, decimal RemainingBalance);

/// <summary>The amount actually applied to one order this month (may be less than its installment if the İİK m.83
/// cap or the remaining balance was smaller) - the Application layer uses this to call
/// <see cref="Garnishments.GarnishmentOrder.ApplyMonthlyDeduction"/> on the matching aggregate.</summary>
public sealed record GarnishmentAppliedAmount(Guid OrderId, decimal Amount);

public sealed record GarnishmentCalculationResult(decimal TotalDeduction, IReadOnlyList<GarnishmentAppliedAmount> AppliedAmounts);

/// <summary>
/// İcra/nafaka kesinti hesabı (İİK m.83, senseik-019 2c "En Yüksek Risk Noktaları #3"). BASİTLEŞTİRME - SMMM/hukuk
/// onayı gerekmeden canlı ortamda kullanılmamalı (bkz. docs/handoffs/payroll-full-engine.md): asgari ücretin
/// haczedilemezliği ayrıca uygulanmıyor, yalnızca net ücret üzerinden hesaplanıyor; nafaka/icra sıralaması ve
/// oranları genel kabul gören yorumu izliyor ama madde bazında doğrulanmadı.
/// Kural: nafaka alacakları öncelikli ve kesinti oranı sınırı yoktur (kalan nete kadar); icra alacakları toplamda
/// net ücretin (kesinti öncesi) %25'ini aşamaz ve bu havuzu emir sırasına göre (FIFO) paylaşır.
/// </summary>
public static class GarnishmentCalculator
{
    public const decimal IcraCapRate = 0.25m;

    public static GarnishmentCalculationResult Calculate(decimal netBeforeDeductions, IReadOnlyList<GarnishmentDeductionInput> orders)
    {
        if (netBeforeDeductions <= 0m || orders.Count == 0)
        {
            return new GarnishmentCalculationResult(0m, []);
        }

        var applied = new List<GarnishmentAppliedAmount>();
        var remainingNet = netBeforeDeductions;

        foreach (var order in orders.Where(o => o.Type == GarnishmentType.Nafaka))
        {
            var amount = Cap(order, remainingNet);
            if (amount <= 0m)
            {
                continue;
            }

            applied.Add(new GarnishmentAppliedAmount(order.OrderId, amount));
            remainingNet -= amount;
        }

        var icraPool = Math.Min(Round(netBeforeDeductions * IcraCapRate), remainingNet);
        foreach (var order in orders.Where(o => o.Type == GarnishmentType.Icra))
        {
            if (icraPool <= 0m)
            {
                break;
            }

            var amount = Cap(order, icraPool);
            if (amount <= 0m)
            {
                continue;
            }

            applied.Add(new GarnishmentAppliedAmount(order.OrderId, amount));
            icraPool -= amount;
            remainingNet -= amount;
        }

        return new GarnishmentCalculationResult(Round(applied.Sum(a => a.Amount)), applied);
    }

    private static decimal Cap(GarnishmentDeductionInput order, decimal limit) =>
        Round(Math.Max(0m, Math.Min(Math.Min(order.MonthlyInstallmentAmount, order.RemainingBalance), limit)));

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
