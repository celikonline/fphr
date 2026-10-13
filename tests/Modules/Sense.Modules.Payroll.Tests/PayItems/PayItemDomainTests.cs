using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>PayItemDefinition and EmployeePayItemAssignment rules (PAY-FORMULA-2). Pure domain, no DB.</summary>
public sealed class PayItemDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly IReadOnlySet<DateOnly> NoClosedMonths = new HashSet<DateOnly>();

    internal static PayItemDefinitionData Data(
        string formula = "deger",
        PayItemAmountBasis basis = PayItemAmountBasis.Gross,
        PayItemDirection direction = PayItemDirection.Earning,
        bool sgk = true,
        bool incomeTax = true,
        bool stampTax = true,
        decimal? defaultValue = null,
        DateOnly? validTo = null,
        string name = "Yemek yardımı") =>
        new(name, direction, basis, sgk, incomeTax, stampTax, formula, defaultValue, Jan, validTo);

    internal static PayItemDefinition Definition(string formula = "deger", decimal? defaultValue = null) =>
        PayItemDefinition.Create(TenantId, "YEMEK", Data(formula, defaultValue: defaultValue)).Value;

    private static HashSet<DateOnly> Closed(params (int Year, int Month)[] months) => months.Select(m => new DateOnly(m.Year, m.Month, 1)).ToHashSet();

    [Theory]
    [InlineData("YEMEK")]
    [InlineData("A1")]
    [InlineData("YAKIT_YARDIMI_2")]
    public void Code_AcceptsTheDocumentedPattern(string code)
    {
        PayItemDefinition.Create(TenantId, code, Data()).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("yemek")]
    [InlineData("1YEMEK")]
    [InlineData("_YEMEK")]
    [InlineData("YEMEK YARDIMI")]
    [InlineData("YEMEK-1")]
    [InlineData("ÇAY")]
    public void Code_RejectsAnythingElse(string code)
    {
        PayItemDefinition.Create(TenantId, code, Data()).Error.Code.ShouldBe(PayrollDomainErrors.PayItemCodeInvalid);
    }

    [Fact]
    public void Code_AllowsAtMost32Characters()
    {
        PayItemDefinition.Create(TenantId, "A" + new string('B', 31), Data()).IsSuccess.ShouldBeTrue();
        PayItemDefinition.Create(TenantId, "A" + new string('B', 32), Data()).Error.Code.ShouldBe(PayrollDomainErrors.PayItemCodeInvalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsRequired(string name)
    {
        PayItemDefinition.Create(TenantId, "YEMEK", Data(name: name)).Error.Code.ShouldBe(PayrollDomainErrors.PayItemNameInvalid);
    }

    [Fact]
    public void Name_AllowsAtMost100Characters()
    {
        PayItemDefinition.Create(TenantId, "YEMEK", Data(name: new string('a', 100))).IsSuccess.ShouldBeTrue();
        PayItemDefinition.Create(TenantId, "YEMEK", Data(name: new string('a', 101))).Error.Code.ShouldBe(PayrollDomainErrors.PayItemNameInvalid);
    }

    [Fact]
    public void Deduction_IsAccepted_AndTaxFlagsAreNormalized()
    {
        // PAY-DEDUCTION: the deduction is accepted; whatever tax flags arrive are stored as false (a deduction is never a taxable earning).
        var created = PayItemDefinition.Create(TenantId, "KESINTI", Data(direction: PayItemDirection.Deduction, sgk: true, incomeTax: true, stampTax: true));

        created.IsSuccess.ShouldBeTrue();
        created.Value.Direction.ShouldBe(PayItemDirection.Deduction);
        created.Value.IsSgkSubject.ShouldBeFalse();
        created.Value.IsIncomeTaxSubject.ShouldBeFalse();
        created.Value.IsStampTaxSubject.ShouldBeFalse();
        created.Value.AmountBasis.ShouldBe(PayItemAmountBasis.Gross);
    }

    [Fact]
    public void Deduction_WithNetBasis_IsRejected_EvenWhenTheFlagsAreFalse()
    {
        var result = PayItemDefinition.Create(TenantId, "KESINTI", Data(direction: PayItemDirection.Deduction, basis: PayItemAmountBasis.Net, sgk: false, incomeTax: false, stampTax: false));

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemDeductionBasisInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Deduction_UpdateNormalizesTheFlagsToo_AndStillValidatesTheFormula()
    {
        var definition = PayItemDefinition.Create(TenantId, "KESINTI", Data(direction: PayItemDirection.Deduction)).Value;

        definition.Update(Data(direction: PayItemDirection.Deduction, formula: "brut * 0.01", sgk: true, incomeTax: true, stampTax: true)).IsSuccess.ShouldBeTrue();
        definition.IsSgkSubject.ShouldBeFalse();
        definition.Update(Data(direction: PayItemDirection.Deduction, formula: "brut *")).IsFailure.ShouldBeTrue();
        definition.Formula.ShouldBe("brut * 0.01");
    }

    [Fact]
    public void Direction_OutsideTheEnum_IsStillNotSupported()
    {
        PayItemDefinition.Create(TenantId, "KESINTI", Data(direction: (PayItemDirection)7)).Error.Code.ShouldBe(PayrollDomainErrors.PayItemDirectionNotSupported);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void NetItem_RequiresAllThreeTaxFlags(bool sgk, bool incomeTax, bool stampTax)
    {
        var result = PayItemDefinition.Create(TenantId, "NETYARDIM", Data(basis: PayItemAmountBasis.Net, sgk: sgk, incomeTax: incomeTax, stampTax: stampTax));

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayItemNetRequiresFullyTaxable);
    }

    [Fact]
    public void NetItem_IsAccepted_WhenFullyTaxable()
    {
        PayItemDefinition.Create(TenantId, "NETYARDIM", Data(basis: PayItemAmountBasis.Net)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void GrossItem_MayBeExemptFromAnyTax()
    {
        var created = PayItemDefinition.Create(TenantId, "MUAF", Data(sgk: false, incomeTax: false, stampTax: false));

        created.IsSuccess.ShouldBeTrue();
        created.Value.IsSgkSubject.ShouldBeFalse();
    }

    [Fact]
    public void ValidTo_BeforeValidFrom_IsRejected()
    {
        PayItemDefinition.Create(TenantId, "YEMEK", Data(validTo: Jan.AddDays(-1))).Error.Code.ShouldBe(PayrollDomainErrors.PayItemDateRangeInvalid);
        PayItemDefinition.Create(TenantId, "YEMEK", Data(validTo: Jan)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Formula_IsValidatedAtSaveTime_WithTheFormulaErrorCode()
    {
        var result = PayItemDefinition.Create(TenantId, "YEMEK", Data(formula: "brut * nope"));

        result.Error.Code.ShouldBe(PayrollDomainErrors.FormulaUnknownVariable);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        PayItemDefinition.Create(TenantId, "YEMEK", Data(formula: "")).Error.Code.ShouldBe(PayrollDomainErrors.FormulaEmpty);
        PayItemDefinition.Create(TenantId, "YEMEK", Data(formula: "1,5")).Error.Code.ShouldBe(PayrollDomainErrors.FormulaDecimalComma);
    }

    [Fact]
    public void Update_KeepsTheCode_AndRevalidates()
    {
        var definition = Definition();

        definition.Update(Data(formula: "brut * 0.1", name: "  Yeni ad  ")).IsSuccess.ShouldBeTrue();
        definition.Code.ShouldBe("YEMEK");
        definition.Name.ShouldBe("Yeni ad");
        definition.Formula.ShouldBe("brut * 0.1");

        definition.Update(Data(direction: PayItemDirection.Deduction, basis: PayItemAmountBasis.Net)).IsFailure.ShouldBeTrue();
        definition.Formula.ShouldBe("brut * 0.1");
    }

    [Fact]
    public void UsesValue_FollowsTheFormula()
    {
        Definition("deger * 2").UsesValue.ShouldBeTrue();
        Definition("brut * 0.1").UsesValue.ShouldBeFalse();
    }

    [Fact]
    public void ActivateDeactivate_AndEffectiveWindow()
    {
        var definition = PayItemDefinition.Create(TenantId, "YEMEK", Data(validTo: new DateOnly(2026, 6, 15))).Value;

        definition.IsActive.ShouldBeTrue();
        definition.IsEffectiveInMonth(new DateOnly(2026, 6, 1)).ShouldBeTrue();
        definition.IsEffectiveInMonth(new DateOnly(2026, 7, 1)).ShouldBeFalse();
        definition.IsEffectiveInMonth(new DateOnly(2025, 12, 1)).ShouldBeFalse();

        definition.Deactivate();
        definition.IsEffectiveInMonth(new DateOnly(2026, 6, 1)).ShouldBeFalse();
        definition.Activate();
        definition.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Assignment_NormalizesToTheFirstOfTheMonth()
    {
        var created = EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 500m, new DateOnly(2026, 3, 17), new DateOnly(2026, 5, 31), NoClosedMonths);

        created.Value.ValidFromMonth.ShouldBe(new DateOnly(2026, 3, 1));
        created.Value.ValidToMonth.ShouldBe(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public void Assignment_OneOff_HasEqualStartAndEnd()
    {
        var created = EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 500m, Jan, Jan, NoClosedMonths);

        created.IsSuccess.ShouldBeTrue();
        created.Value.IsCovered(Jan).ShouldBeTrue();
        created.Value.IsCovered(Jan.AddMonths(1)).ShouldBeFalse();
    }

    [Fact]
    public void Assignment_EndBeforeStart_IsRejected()
    {
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 1m, new DateOnly(2026, 3, 1), new DateOnly(2026, 2, 1), NoClosedMonths)
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemDateRangeInvalid);
    }

    [Fact]
    public void Assignment_InAClosedMonth_IsRejected()
    {
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 1m, Jan, null, Closed((2026, 1)))
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
    }

    [Fact]
    public void Assignment_CoveringALaterClosedMonth_IsRejected_EvenWhenTheStartIsOpen()
    {
        // Reopened January, closed February: an open-ended or Jan..Mar range would cover February.
        var closed = Closed((2026, 2));
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 1m, Jan, null, closed).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 1m, Jan, Jan.AddMonths(2), closed).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 1m, Jan, Jan, closed).IsSuccess.ShouldBeTrue();
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 1m, Jan.AddMonths(2), null, closed).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Assignment_StartingAfterAClosedMonth_IsAllowed()
    {
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 1m, Jan.AddMonths(1), null, Closed((2026, 1))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Assignment_OfAnInactiveItem_IsRejected()
    {
        var definition = Definition();
        definition.Deactivate();

        EmployeePayItemAssignment.Create(TenantId, EmployeeId, definition, 1m, Jan, null, NoClosedMonths).Error.Code.ShouldBe(PayrollDomainErrors.PayItemInactive);
    }

    [Fact]
    public void Assignment_NeedsAValue_WhenTheFormulaUsesDegerAndNoDefaultExists()
    {
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition("deger"), null, Jan, null, NoClosedMonths)
            .Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentValueRequired);
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition("deger", defaultValue: 250m), null, Jan, null, NoClosedMonths).IsSuccess.ShouldBeTrue();
        EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition("brut * 0.1"), null, Jan, null, NoClosedMonths).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_OfAnUnusedAssignment_ChangesValueAndEnd()
    {
        var definition = Definition();
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeId, definition, 100m, Jan, null, NoClosedMonths).Value;

        assignment.Update(definition, 200m, Jan.AddMonths(3), NoClosedMonths).IsSuccess.ShouldBeTrue();

        assignment.Value.ShouldBe(200m);
        assignment.ValidToMonth.ShouldBe(Jan.AddMonths(3));
    }

    [Fact]
    public void Update_OfAUsedAssignment_CanOnlyBeEnded_NotRevalued()
    {
        var definition = Definition();
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeId, definition, 100m, Jan, null, NoClosedMonths).Value;
        var closed = Closed((2026, 1), (2026, 2));

        assignment.Update(definition, 999m, null, closed).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        assignment.Update(definition, 100m, Jan, closed).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
        assignment.Update(definition, 100m, Jan.AddMonths(1), closed).IsSuccess.ShouldBeTrue();
        assignment.ValidToMonth.ShouldBe(Jan.AddMonths(1));
        assignment.Value.ShouldBe(100m);
    }

    [Fact]
    public void Update_CannotExtendAnEndedAssignmentOverAClosedMonth()
    {
        var definition = Definition();
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeId, definition, 100m, Jan, Jan, NoClosedMonths).Value;

        assignment.Update(definition, 100m, Jan.AddMonths(2), Closed((2026, 1), (2026, 2))).Error.Code
            .ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
    }

    [Fact]
    public void Update_StillWorksForAnInactiveDefinition_SoUsedAssignmentsCanBeEnded()
    {
        var definition = Definition();
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeId, definition, 100m, Jan, null, NoClosedMonths).Value;
        definition.Deactivate();

        assignment.Update(definition, 100m, Jan.AddMonths(1), Closed((2026, 1))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Delete_IsRefused_OnceAnyCoveredMonthIsClosed()
    {
        var assignment = EmployeePayItemAssignment.Create(TenantId, EmployeeId, Definition(), 100m, Jan, null, NoClosedMonths).Value;

        assignment.EnsureDeletable(NoClosedMonths).IsSuccess.ShouldBeTrue();
        assignment.EnsureDeletable(Closed((2025, 12))).IsSuccess.ShouldBeTrue();
        assignment.EnsureDeletable(Closed((2026, 4))).Error.Code.ShouldBe(PayrollDomainErrors.PayItemAssignmentPeriodClosed);
    }

    private static List<EmployeePayItemAssignment> Existing(int count, DateOnly from, DateOnly? to)
    {
        var definition = Definition();
        return Enumerable.Range(0, count)
            .Select(_ => EmployeePayItemAssignment.Create(TenantId, EmployeeId, definition, 1m, from, to, NoClosedMonths).Value)
            .ToList();
    }

    [Fact]
    public void MonthlyLimit_Allows50_AndRefusesThe51st()
    {
        ExceedsMonthlyLimit(Existing(49, Jan, null)).ShouldBeFalse();
        ExceedsMonthlyLimit(Existing(50, Jan, null)).ShouldBeTrue();

        static bool ExceedsMonthlyLimit(List<EmployeePayItemAssignment> existing) => EmployeePayItemAssignment.ExceedsMonthlyLimit(existing, Jan, null);
    }

    [Fact]
    public void MonthlyLimit_CountsOnlyOverlappingMonths()
    {
        var january = Existing(50, Jan, Jan);

        EmployeePayItemAssignment.ExceedsMonthlyLimit(january, Jan.AddMonths(1), null).ShouldBeFalse();
        EmployeePayItemAssignment.ExceedsMonthlyLimit(january, Jan.AddMonths(-1), Jan).ShouldBeTrue();
    }

    [Fact]
    public void MonthlyLimit_SeesALaterStartInsideTheNewRange()
    {
        var existing = Existing(25, Jan, null);
        existing.AddRange(Existing(25, Jan.AddMonths(6), null));

        // Jan..Dec new range: from July on there are 50 existing already.
        EmployeePayItemAssignment.ExceedsMonthlyLimit(existing, Jan, Jan.AddMonths(11)).ShouldBeTrue();
        EmployeePayItemAssignment.ExceedsMonthlyLimit(existing, Jan, Jan.AddMonths(5)).ShouldBeFalse();
    }
}
