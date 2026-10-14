using System.Text.Json;
using NSubstitute;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Lines of an <see cref="ExitSettlement"/>: the automatic kıdem/ihbar/izin lines, manual additions (ikale bedeli, ek ödeme),
/// and the net that <see cref="ExitSettlementNetService"/> stores for them.</summary>
public sealed class ExitSettlementLineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly TerminationDate = new(2026, 8, 17);
    private static readonly ExitPayTaxTreatment Taxed = new(false, true, true);

    private static ExitSettlement Settlement(decimal severance = 0m, decimal notice = 0m, decimal leave = 0m) =>
        ExitSettlement.Create(
            TenantId,
            new ExitSettlementData(EmployeeId, TerminationDate, SeveranceCauseCategory.EmployerNoticeBased, "TRY", 400, 2_000m, severance, notice, leave, severance + notice + leave, "[]"),
            DateTimeOffset.UtcNow);

    [Fact]
    public void Create_MakesAnAutomaticLine_ForEachNonZeroAmount()
    {
        var settlement = Settlement(severance: 100m, leave: 50m);

        settlement.Lines.Select(l => l.Kind).ShouldBe([ExitPayKind.Severance, ExitPayKind.UnusedLeave]);
        settlement.Lines.ShouldAllBe(l => l.IsAutomatic);
    }

    [Fact]
    public void AddLine_AddsAManualLine_AndRaisesTheGrossTotal_AndMarksTheNetStale()
    {
        var settlement = Settlement(notice: 1_000m);

        var added = settlement.AddLine(ExitPayKind.MutualRescissionPay, "İkale bedeli", 4_000m, Taxed);

        added.IsSuccess.ShouldBeTrue();
        added.Value.IsAutomatic.ShouldBeFalse();
        settlement.TotalAmount.ShouldBe(5_000m);
        settlement.NetCalculated.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("  ", 10)]
    [InlineData("Ek ödeme", 0)]
    [InlineData("Ek ödeme", -5)]
    public void AddLine_RejectsAMissingNameOrANonPositiveAmount(string label, int amount)
    {
        var result = Settlement().AddLine(ExitPayKind.ExtraPayment, label, amount, Taxed);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.ExitSettlementLineInvalid);
    }

    [Fact]
    public void UpdateLine_OverridesAnAutomaticLine_KeepingItsEmptyName()
    {
        var settlement = Settlement(notice: 1_000m);
        var notice = settlement.Lines.Single();

        var result = settlement.UpdateLine(notice.Id, string.Empty, 1_200m, Taxed);

        result.IsSuccess.ShouldBeTrue();
        notice.GrossAmount.ShouldBe(1_200m);
        settlement.TotalAmount.ShouldBe(1_200m);
    }

    [Fact]
    public void UpdateLine_RejectsAnEmptyNameOnAManualLine()
    {
        var settlement = Settlement();
        var manual = settlement.AddLine(ExitPayKind.ExtraPayment, "Ek ödeme", 100m, Taxed).Value;

        settlement.UpdateLine(manual.Id, " ", 100m, Taxed).Error.Code.ShouldBe(PayrollDomainErrors.ExitSettlementLineInvalid);
    }

    [Fact]
    public void RemoveLine_RemovesAManualLine_ButNeverAnAutomaticOne()
    {
        var settlement = Settlement(notice: 1_000m);
        var manual = settlement.AddLine(ExitPayKind.ExtraPayment, "Ek ödeme", 500m, Taxed).Value;

        settlement.RemoveLine(settlement.Lines.First(l => l.IsAutomatic).Id).Error.Code.ShouldBe(PayrollDomainErrors.ExitSettlementLineNotRemovable);
        settlement.RemoveLine(manual.Id).IsSuccess.ShouldBeTrue();
        settlement.RemoveLine(Guid.NewGuid()).Error.Code.ShouldBe(PayrollDomainErrors.ExitSettlementLineNotFound);
        settlement.TotalAmount.ShouldBe(1_000m);
    }

    private static ILegislationParameterReader PayrollLegislation()
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        void Decimal(string key, decimal value) =>
            reader.FindAsync(key, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<LegislationParameterValue?>(new LegislationParameterValue(key, "TR", value, null, new DateOnly(2020, 1, 1), null, "test")));

        reader.FindAsync(LegislationParameterKeys.PayrollIncomeTaxBrackets, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LegislationParameterValue?>(new LegislationParameterValue(
                LegislationParameterKeys.PayrollIncomeTaxBrackets, "TR", null,
                JsonSerializer.Serialize(new[] { new { upperBound = (decimal?)100_000m, rate = 0.15m }, new { upperBound = (decimal?)null, rate = 0.20m } }),
                new DateOnly(2020, 1, 1), null, "test")));
        Decimal(LegislationParameterKeys.PayrollSgkFloor, 0m);
        Decimal(LegislationParameterKeys.PayrollSgkCeiling, 100_000m);
        Decimal(LegislationParameterKeys.PayrollSgkEmployeeRate, 0.14m);
        Decimal(LegislationParameterKeys.PayrollUnemploymentEmployeeRate, 0.01m);
        Decimal(LegislationParameterKeys.PayrollSgkEmployerRate, 0m);
        Decimal(LegislationParameterKeys.PayrollUnemploymentEmployerRate, 0m);
        Decimal(LegislationParameterKeys.PayrollSgkEmployerDiscountRate, 0m);
        Decimal(LegislationParameterKeys.PayrollStampTaxRate, 0.00759m);
        Decimal(LegislationParameterKeys.PayrollMinimumWageGross, 0m);
        Decimal(LegislationParameterKeys.PayrollMealIncomeTaxDailyExemption, 0m);
        Decimal(LegislationParameterKeys.PayrollMealSgkDailyExemption, 0m);
        Decimal(LegislationParameterKeys.PayrollMealMarketCardExemptionApplies, 0m);
        Decimal(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree1, 0m);
        Decimal(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree2, 0m);
        Decimal(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree3, 0m);
        return reader;
    }

    [Fact]
    public async Task NetService_NetsEveryLine_AndCarriesOverTheGvBaseOfTheLatestEarlierPayslip()
    {
        // Ağustos çıkışı: Haziran'da hesaplanmış bordro var (Temmuz dönemi yok), kümülatif matrah 90,000 devreder.
        var june = PayrollPeriod.Create(TenantId, 2026, 6).Value;
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByYearMonthAsync(2026, 6, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(june));
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.FindByEmployeeAndPeriodAsync(EmployeeId, june.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payslip?>(PayslipWithCumulativeBase(june.Id, 90_000m)));
        var service = new ExitSettlementNetService(PayrollLegislation(), periods, payslips);
        var settlement = Settlement(notice: 20_000m);

        var result = await service.RecalculateAsync(settlement, CancellationToken.None);

        // 90,000 + 20,000 = 110,000 -> tax 15,000 + 10,000*20% - 13,500 = 3,500 (bracket crossing); damga 20,000 * 0.759% = 151.80.
        result.IsSuccess.ShouldBeTrue();
        settlement.NetCalculated.ShouldBeTrue();
        settlement.IncomeTax.ShouldBe(3_500m);
        settlement.StampTax.ShouldBe(151.80m);
        settlement.NetTotal.ShouldBe(16_348.20m);
        settlement.Lines.Single().NetAmount.ShouldBe(16_348.20m);
    }

    [Fact]
    public async Task NetService_WhenThePayrollLegislationIsMissing_LeavesTheSettlementUncalculated()
    {
        var service = new ExitSettlementNetService(Substitute.For<ILegislationParameterReader>(), Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IPayslipRepository>());
        var settlement = Settlement(notice: 1_000m);

        var result = await service.RecalculateAsync(settlement, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.LegislationParameterMissing);
        settlement.NetCalculated.ShouldBeFalse();
    }

    private static Payslip PayslipWithCumulativeBase(Guid periodId, decimal cumulativeGvBase)
    {
        var payslip = (Payslip)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Payslip));
        typeof(Payslip).GetProperty(nameof(Payslip.CumulativeGvBase))!.SetValue(payslip, cumulativeGvBase);
        typeof(Payslip).GetProperty(nameof(Payslip.PayrollPeriodId))!.SetValue(payslip, periodId);
        return payslip;
    }
}
