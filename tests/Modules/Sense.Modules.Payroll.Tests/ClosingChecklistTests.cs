using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Closing;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Closing;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>The closing checklist: opened from the catalog, automatic items decided by rules, manual ones ticked off or waived.</summary>
public sealed class ClosingChecklistTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public PayrollPeriod Period { get; } = PayrollPeriod.Create(TenantId, 2026, 8).Value;

        public List<PeriodClosingCheck> Stored { get; } = [];

        public List<Payslip> Payslips { get; } = [];

        public int DirtyFlags { get; set; }

        public bool LegislationPresent { get; set; }

        public List<Guid> ActiveEmployees { get; } = [];

        public ClosingChecklistService Service()
        {
            var checks = Substitute.For<IPeriodClosingCheckRepository>();
            checks.ListByPeriodAsync(Period.Id, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<PeriodClosingCheck>>(Stored.ToList()));
            checks.When(c => c.Add(Arg.Any<PeriodClosingCheck>())).Do(call => Stored.Add(call.Arg<PeriodClosingCheck>()));

            var payslips = Substitute.For<IPayslipRepository>();
            payslips.ListByPeriodAsync(Period.Id, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<Payslip>>(Payslips.ToList()));

            var flags = Substitute.For<IPayslipDirtyFlagRepository>();
            flags.ListActiveByPeriodAsync(Period.Id, Arg.Any<CancellationToken>()).Returns(_ =>
                Task.FromResult<IReadOnlyList<PayslipDirtyFlag>>(Enumerable.Range(0, DirtyFlags)
                    .Select(__ => (PayslipDirtyFlag)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PayslipDirtyFlag))).ToList()));

            var directory = Substitute.For<IEmployeeDirectory>();
            directory.ListActiveEmployeeIdsAsync(TenantId, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<Guid>>(ActiveEmployees.ToList()));

            return new ClosingChecklistService(checks, payslips, flags, Legislation(), directory, new FixedClock(Now));
        }

        private ILegislationParameterReader Legislation()
        {
            var reader = Substitute.For<ILegislationParameterReader>();
            if (!LegislationPresent)
            {
                return reader;
            }

            void Decimal(string key, decimal value) =>
                reader.FindAsync(key, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<LegislationParameterValue?>(new LegislationParameterValue(key, "TR", value, null, new DateOnly(2020, 1, 1), null, "test")));

            reader.FindAsync(LegislationParameterKeys.PayrollIncomeTaxBrackets, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<LegislationParameterValue?>(new LegislationParameterValue(
                    LegislationParameterKeys.PayrollIncomeTaxBrackets, "TR", null, """[{"upperBound":null,"rate":0.15}]""", new DateOnly(2020, 1, 1), null, "test")));
            foreach (var key in new[]
            {
                LegislationParameterKeys.PayrollSgkFloor, LegislationParameterKeys.PayrollSgkCeiling, LegislationParameterKeys.PayrollSgkEmployeeRate,
                LegislationParameterKeys.PayrollUnemploymentEmployeeRate, LegislationParameterKeys.PayrollSgkEmployerRate, LegislationParameterKeys.PayrollUnemploymentEmployerRate,
                LegislationParameterKeys.PayrollSgkEmployerDiscountRate, LegislationParameterKeys.PayrollStampTaxRate, LegislationParameterKeys.PayrollMinimumWageGross,
                LegislationParameterKeys.PayrollMealIncomeTaxDailyExemption, LegislationParameterKeys.PayrollMealSgkDailyExemptionRate, LegislationParameterKeys.PayrollMealMarketCardExemptionApplies,
                LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree1, LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree2, LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree3,
            })
            {
                Decimal(key, 1m);
            }

            return reader;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Payslip PayslipOf(decimal gross, decimal net)
    {
        var payslip = (Payslip)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Payslip));
        typeof(Payslip).GetProperty(nameof(Payslip.GrossAmount))!.SetValue(payslip, gross);
        typeof(Payslip).GetProperty(nameof(Payslip.NetAmount))!.SetValue(payslip, net);
        return payslip;
    }

    private static async Task<PeriodClosingCheck> RunAndFind(Fixture f, string code)
    {
        var service = f.Service();
        var list = await service.EnsureAsync(f.Period, CancellationToken.None);
        await service.RunAutomaticAsync(f.Period, list, CancellationToken.None);
        return list.Single(c => c.Code == code);
    }

    [Fact]
    public async Task Ensure_OpensTheWholeCatalog_AndIsIdempotent()
    {
        var f = new Fixture();
        var service = f.Service();

        var first = await service.EnsureAsync(f.Period, CancellationToken.None);
        var second = await service.EnsureAsync(f.Period, CancellationToken.None);

        first.Count.ShouldBe(ClosingCheckCatalog.All.Count);
        second.Count.ShouldBe(ClosingCheckCatalog.All.Count);
        f.Stored.Count.ShouldBe(ClosingCheckCatalog.All.Count);
        first.ShouldAllBe(c => c.Status == ClosingCheckStatus.Pending);
    }

    [Fact]
    public async Task PayslipsGenerated_PassesOnlyOnceThePeriodHasPayslips()
    {
        var f = new Fixture();
        (await RunAndFind(f, "PAYSLIPS_GENERATED")).Status.ShouldBe(ClosingCheckStatus.Failed);

        f.Stored.Clear();
        f.Payslips.Add(PayslipOf(50_000m, 38_000m));
        var passed = await RunAndFind(f, "PAYSLIPS_GENERATED");

        passed.Status.ShouldBe(ClosingCheckStatus.Passed);
        passed.Detail.ShouldBe("1");
    }

    [Fact]
    public async Task NoDirtyPayslips_FailsWithTheNumberOfChangedPayslips()
    {
        var f = new Fixture { DirtyFlags = 3 };

        var check = await RunAndFind(f, "NO_DIRTY_PAYSLIPS");

        check.Status.ShouldBe(ClosingCheckStatus.Failed);
        check.Detail.ShouldBe("3");
    }

    [Fact]
    public async Task NetNotNegative_FailsForANegativeNetOrANonPositiveGross()
    {
        var f = new Fixture();
        f.Payslips.AddRange([PayslipOf(50_000m, 38_000m), PayslipOf(50_000m, -10m), PayslipOf(0m, 0m)]);

        var check = await RunAndFind(f, "NET_NOT_NEGATIVE");

        check.Status.ShouldBe(ClosingCheckStatus.Failed);
        check.Detail.ShouldBe("2");
    }

    [Fact]
    public async Task LegislationParameters_FailsNamingTheMissingParameter_AndPassesWhenTheyExist()
    {
        var missing = await RunAndFind(new Fixture(), "LEGISLATION_PARAMETERS");
        missing.Status.ShouldBe(ClosingCheckStatus.Failed);
        missing.Detail.ShouldBe(LegislationParameterKeys.PayrollIncomeTaxBrackets);

        var present = await RunAndFind(new Fixture { LegislationPresent = true }, "LEGISLATION_PARAMETERS");
        present.Status.ShouldBe(ClosingCheckStatus.Passed);
    }

    [Fact]
    public async Task EmployeesWithoutPayslip_WarnsAboutActiveEmployeesTheRunSkipped_ButDoesNotBlock()
    {
        var f = new Fixture();
        var paidEmployee = Guid.NewGuid();
        var skippedEmployee = Guid.NewGuid();
        f.ActiveEmployees.AddRange([paidEmployee, skippedEmployee]);
        var paid = PayslipOf(50_000m, 38_000m);
        typeof(Payslip).GetProperty(nameof(Payslip.EmployeeId))!.SetValue(paid, paidEmployee);
        f.Payslips.Add(paid);

        var check = await RunAndFind(f, "EMPLOYEES_WITHOUT_PAYSLIP");

        check.Status.ShouldBe(ClosingCheckStatus.Failed);
        check.Detail.ShouldBe("1");
        check.Severity.ShouldBe(ClosingCheckSeverity.Warning);
    }

    [Fact]
    public async Task Waive_KeepsAFailedBlockerWaived_EvenWhenTheRulesRunAgain()
    {
        var f = new Fixture();
        var service = f.Service();
        var list = await service.EnsureAsync(f.Period, CancellationToken.None);
        var check = list.Single(c => c.Code == "PAYSLIPS_GENERATED");

        check.Waive("Bu ay bordro dışı çalıştı", Guid.NewGuid(), Now).IsSuccess.ShouldBeTrue();
        await service.RunAutomaticAsync(f.Period, list, CancellationToken.None);

        check.Status.ShouldBe(ClosingCheckStatus.Waived);
        check.IsSatisfied.ShouldBeTrue();
        check.WaiverReason.ShouldBe("Bu ay bordro dışı çalıştı");
    }

    [Fact]
    public async Task Waive_NeedsAReason()
    {
        var f = new Fixture();
        var list = await f.Service().EnsureAsync(f.Period, CancellationToken.None);

        list[0].Waive("  ", null, Now).Error.Code.ShouldBe(PayrollDomainErrors.ClosingWaiverReasonInvalid);
    }

    [Fact]
    public async Task Complete_TicksAManualCheckOff_ButNotAnAutomaticOne()
    {
        var f = new Fixture();
        var list = await f.Service().EnsureAsync(f.Period, CancellationToken.None);
        var manual = list.First(c => c.Kind == ClosingCheckKind.Manual);
        var automatic = list.First(c => c.Kind == ClosingCheckKind.Automatic);

        manual.Complete("  kontrol edildi  ", null, Now).IsSuccess.ShouldBeTrue();
        automatic.Complete(null, null, Now).Error.Code.ShouldBe(PayrollDomainErrors.ClosingCheckNotManual);

        manual.Status.ShouldBe(ClosingCheckStatus.Passed);
        manual.Detail.ShouldBe("kontrol edildi");
        automatic.Status.ShouldBe(ClosingCheckStatus.Pending);
    }

    [Fact]
    public async Task OpenBlockers_AreReported_UntilEveryAutomaticBlockerIsPassedOrWaived()
    {
        var f = new Fixture();
        var service = f.Service();
        (await service.HasOpenBlockersBeforeApprovalAsync(f.Period, CancellationToken.None)).ShouldBeTrue();

        foreach (var blocker in f.Stored.Where(c => c.Severity == ClosingCheckSeverity.Blocker))
        {
            blocker.Waive("Denetim için muaf", null, Now);
        }

        (await service.HasOpenBlockersBeforeApprovalAsync(f.Period, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public void TheCatalog_HasUniqueCodes_AndOnlyTheAutomaticItemsBlock()
    {
        ClosingCheckCatalog.All.Select(d => d.Code).ShouldBeUnique();
        ClosingCheckCatalog.All.Where(d => d.Severity == ClosingCheckSeverity.Blocker).ShouldAllBe(d => d.Kind == ClosingCheckKind.Automatic);
        ClosingCheckCatalog.All.Select(d => d.Stage).Distinct().Count().ShouldBe(4);
    }
}
