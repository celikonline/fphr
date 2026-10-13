using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>EmployeeIncentiveAssignment rules (PAY-INCENTIVE-1): closed months, category overlap, max months, declaration code. Pure domain.</summary>
public sealed class IncentiveAssignmentTests
{
    private static readonly Guid TenantId = IncentiveDefinitionTests.TenantId;
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly IReadOnlySet<DateOnly> NoClosedMonths = new HashSet<DateOnly>();

    private static DateOnly M(int year, int month) => new(year, month, 1);

    private static HashSet<DateOnly> Closed(params (int Year, int Month)[] months) => months.Select(m => M(m.Year, m.Month)).ToHashSet();

    /// <summary>Resolves categories for the definitions used in a test.</summary>
    private sealed class Catalog
    {
        private readonly Dictionary<Guid, IncentiveDefinition> _definitions = [];

        public IncentiveDefinition Add(IncentiveDefinition definition)
        {
            _definitions[definition.Id] = definition;
            return definition;
        }

        public IncentiveCategory CategoryOf(Guid id) => _definitions[id].Category;
    }

    private static EmployeeIncentiveAssignment Assign(
        Catalog catalog,
        IncentiveDefinition definition,
        DateOnly from,
        DateOnly? to,
        params EmployeeIncentiveAssignment[] existing)
    {
        catalog.Add(definition);
        return EmployeeIncentiveAssignment.Create(TenantId, EmployeeId, definition, from, to, null, NoClosedMonths, existing, catalog.CategoryOf).Value;
    }

    private static Result<EmployeeIncentiveAssignment> TryAssign(
        Catalog catalog,
        IncentiveDefinition definition,
        DateOnly from,
        DateOnly? to,
        IReadOnlySet<DateOnly>? closed = null,
        string? note = null,
        params EmployeeIncentiveAssignment[] existing)
    {
        catalog.Add(definition);
        return EmployeeIncentiveAssignment.Create(TenantId, EmployeeId, definition, from, to, note, closed ?? NoClosedMonths, existing, catalog.CategoryOf);
    }

    [Fact]
    public void Create_NormalizesDatesToTheFirstOfTheMonth_AndStoresTheNote()
    {
        var catalog = new Catalog();

        var assignment = TryAssign(catalog, IncentiveDefinitionTests.Definition(), new DateOnly(2026, 3, 17), new DateOnly(2026, 5, 31), note: "  uygun  ").Value;

        assignment.ValidFromMonth.ShouldBe(M(2026, 3));
        assignment.ValidToMonth.ShouldBe(M(2026, 5));
        assignment.Note.ShouldBe("uygun");
        assignment.MonthCount.ShouldBe(3);
    }

    [Fact]
    public void Create_ForAnUnapprovedDefinition_IsAllowed()
    {
        var definition = IncentiveDefinitionTests.Definition();

        definition.IsSmmmApproved.ShouldBeFalse();
        TryAssign(new Catalog(), definition, M(2026, 1), null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_ForAnInactiveDefinition_IsRejected()
    {
        var definition = IncentiveDefinitionTests.Definition();
        definition.Deactivate();

        TryAssign(new Catalog(), definition, M(2026, 1), null).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveInactive);
    }

    [Fact]
    public void Create_EndBeforeStart_IsRejected()
    {
        TryAssign(new Catalog(), IncentiveDefinitionTests.Definition(), M(2026, 5), M(2026, 4)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveDateRangeInvalid);
    }

    [Fact]
    public void Create_NoteOver500Characters_IsRejected()
    {
        TryAssign(new Catalog(), IncentiveDefinitionTests.Definition(), M(2026, 1), null, note: new string('n', 501)).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
    }

    [Fact]
    public void Create_SgkCategoryWithoutDeclarationCode_IsRejected_ButTaxCategoryIsNot()
    {
        var sgk = IncentiveDefinitionTests.Definition(declarationCode: null);
        var tax = IncentiveDefinitionTests.Definition(IncentiveKind.IncomeTaxWithholdingRelief, "VERGI", declarationCode: null);

        TryAssign(new Catalog(), sgk, M(2026, 1), null).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveDeclarationCodeRequired);
        TryAssign(new Catalog(), tax, M(2026, 1), null).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(2026, 1, 2026, 3)]
    [InlineData(2026, 1, null, null)]
    [InlineData(2025, 12, 2026, 2)]
    public void Create_CoveringAnyClosedMonth_IsRejected(int fy, int fm, int? ty, int? tm)
    {
        var closed = Closed((2026, 2));
        DateOnly? to = ty is { } y ? M(y, tm!.Value) : null;
        TryAssign(new Catalog(), IncentiveDefinitionTests.Definition(), M(fy, fm), to, closed).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
    }

    [Fact]
    public void Create_EndingBeforeTheClosedMonth_IsAllowed()
    {
        TryAssign(new Catalog(), IncentiveDefinitionTests.Definition(), M(2026, 1), M(2026, 1), Closed((2026, 2))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_StartingAfterTheClosedMonths_IsAllowed()
    {
        TryAssign(new Catalog(), IncentiveDefinitionTests.Definition(), M(2026, 4), null, Closed((2026, 2), (2026, 3))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_ClosedMiddleMonthOfARange_IsRejected()
    {
        TryAssign(new Catalog(), IncentiveDefinitionTests.Definition(), M(2026, 1), M(2026, 6), Closed((2026, 3))).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
    }

    [Fact]
    public void CategoryOverlap_AppliesWithinTheSameCategory_NotAcrossCategories()
    {
        var catalog = new Catalog();
        var a = IncentiveDefinitionTests.Definition(IncentiveKind.EmployerSgkRateDiscount, "A_DISC");
        var b = IncentiveDefinitionTests.Definition(IncentiveKind.EmployerShareCoverage, "B_COVER");
        var c = IncentiveDefinitionTests.Definition(IncentiveKind.IncomeTaxWithholdingRelief, "C_TAX");
        var d = IncentiveDefinitionTests.Definition(IncentiveKind.DeclarationOnly, "D_DECL");
        var existingA = Assign(catalog, a, M(2026, 1), M(2026, 6));

        // b and d are SGK category as well: they collide with a; c (tax) does not.
        TryAssign(catalog, b, M(2026, 3), M(2026, 4), existing: existingA).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        TryAssign(catalog, b, M(2026, 3), M(2026, 4), existing: existingA).Error.Type.ShouldBe(ErrorType.Conflict);
        TryAssign(catalog, d, M(2026, 6), null, existing: existingA).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        TryAssign(catalog, a, M(2025, 1), M(2026, 1), existing: existingA).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        TryAssign(catalog, c, M(2026, 3), M(2026, 4), existing: existingA).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CategoryOverlap_AllowsAdjacentRanges()
    {
        var catalog = new Catalog();
        var a = IncentiveDefinitionTests.Definition(IncentiveKind.EmployerSgkRateDiscount, "A_DISC");
        var b = IncentiveDefinitionTests.Definition(IncentiveKind.EmployerShareCoverage, "B_COVER");
        var existing = Assign(catalog, a, M(2026, 1), M(2026, 6));

        TryAssign(catalog, b, M(2026, 7), null, existing: existing).IsSuccess.ShouldBeTrue();
        TryAssign(catalog, b, M(2025, 1), M(2025, 12), existing: existing).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CategoryOverlap_OpenEndedExisting_BlocksEverythingFromItsStart()
    {
        var catalog = new Catalog();
        var a = IncentiveDefinitionTests.Definition(IncentiveKind.EmployerSgkRateDiscount, "A_DISC");
        var existing = Assign(catalog, a, M(2026, 5), null);

        TryAssign(catalog, a, M(2030, 1), M(2030, 2), existing: existing).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        TryAssign(catalog, a, M(2026, 1), M(2026, 4), existing: existing).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void MaxMonths_RequiresAnEndMonth()
    {
        var definition = IncentiveDefinitionTests.Definition(maxMonths: 12);

        TryAssign(new Catalog(), definition, M(2026, 1), null).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentEndRequired);
        TryAssign(new Catalog(), definition, M(2026, 1), M(2026, 12)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void MaxMonths_OverTheLimit_IsRejected()
    {
        var definition = IncentiveDefinitionTests.Definition(maxMonths: 12);

        var error = TryAssign(new Catalog(), definition, M(2026, 1), M(2027, 1)).Error;

        error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentExceedsMaxMonths);
        error.Args!["maxMonths"].ShouldBe(12);
    }

    [Fact]
    public void MaxMonths_CountsTheEmployeesTotalMonthsOfTheSameDefinition()
    {
        var catalog = new Catalog();
        var definition = IncentiveDefinitionTests.Definition(maxMonths: 12);
        var first = Assign(catalog, definition, M(2026, 1), M(2026, 8));

        // 8 months used; 4 left.
        TryAssign(catalog, definition, M(2027, 1), M(2027, 4), existing: first).IsSuccess.ShouldBeTrue();
        TryAssign(catalog, definition, M(2027, 1), M(2027, 5), existing: first).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentExceedsMaxMonths);
    }

    [Fact]
    public void MaxMonths_DoesNotCountOtherDefinitions()
    {
        var catalog = new Catalog();
        var limited = IncentiveDefinitionTests.Definition(maxMonths: 6, code: "LIMITED");
        var other = IncentiveDefinitionTests.Definition(code: "OTHER");
        var existing = Assign(catalog, other, M(2026, 1), M(2026, 12));

        TryAssign(catalog, limited, M(2027, 1), M(2027, 6), existing: existing).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_ExtendingTheEnd_RespectsMaxMonthsExcludingItself()
    {
        var catalog = new Catalog();
        var definition = IncentiveDefinitionTests.Definition(maxMonths: 12);
        var assignment = Assign(catalog, definition, M(2026, 1), M(2026, 6));

        assignment.Update(definition, M(2026, 12), null, NoClosedMonths, [assignment], catalog.CategoryOf).IsSuccess.ShouldBeTrue();
        assignment.Update(definition, M(2027, 1), null, NoClosedMonths, [assignment], catalog.CategoryOf).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveAssignmentExceedsMaxMonths);
        assignment.ValidToMonth.ShouldBe(M(2026, 12));
    }

    [Fact]
    public void Update_RemovingTheEnd_OfALimitedDefinition_IsRejected()
    {
        var catalog = new Catalog();
        var definition = IncentiveDefinitionTests.Definition(maxMonths: 12);
        var assignment = Assign(catalog, definition, M(2026, 1), M(2026, 6));

        assignment.Update(definition, null, null, NoClosedMonths, [assignment], catalog.CategoryOf).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentEndRequired);
    }

    [Fact]
    public void Update_ExtendingIntoAnotherAssignmentOfTheSameCategory_IsAConflict()
    {
        var catalog = new Catalog();
        var a = IncentiveDefinitionTests.Definition(IncentiveKind.EmployerSgkRateDiscount, "A_DISC");
        var b = IncentiveDefinitionTests.Definition(IncentiveKind.EmployerShareCoverage, "B_COVER");
        var first = Assign(catalog, a, M(2026, 1), M(2026, 3));
        var second = Assign(catalog, b, M(2026, 6), M(2026, 8), first);

        var result = first.Update(a, M(2026, 6), null, NoClosedMonths, [first, second], catalog.CategoryOf);

        result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentOverlap);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        first.Update(a, M(2026, 5), null, NoClosedMonths, [first, second], catalog.CategoryOf).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_CannotCutOffOrNewlyCoverAClosedMonth()
    {
        var catalog = new Catalog();
        var definition = IncentiveDefinitionTests.Definition();
        var assignment = Assign(catalog, definition, M(2026, 1), M(2026, 4));
        var closed = Closed((2026, 2), (2026, 4));

        // Cut off closed April.
        assignment.Update(definition, M(2026, 3), null, closed, [assignment], catalog.CategoryOf).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);

        // Newly cover a closed month beyond the current end.
        var shorter = Assign(catalog, definition, M(2026, 1), M(2026, 1));
        shorter.Update(definition, M(2026, 3), null, closed, [shorter], catalog.CategoryOf).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);

        // Ending after the last closed month is fine.
        assignment.Update(definition, M(2026, 6), "uzatıldı", closed, [assignment], catalog.CategoryOf).IsSuccess.ShouldBeTrue();
        assignment.ValidToMonth.ShouldBe(M(2026, 6));
        assignment.Note.ShouldBe("uzatıldı");
    }

    [Fact]
    public void Update_OfAnAssignmentWithNoClosedMonth_CanShortenFreely()
    {
        var catalog = new Catalog();
        var definition = IncentiveDefinitionTests.Definition();
        var assignment = Assign(catalog, definition, M(2026, 3), null);

        assignment.Update(definition, M(2026, 5), null, Closed((2026, 1)), [assignment], catalog.CategoryOf).IsSuccess.ShouldBeTrue();
        assignment.ValidToMonth.ShouldBe(M(2026, 5));
    }

    [Fact]
    public void EnsureDeletable_IsFalseWhenAnyCoveredMonthIsClosed()
    {
        var catalog = new Catalog();
        var assignment = Assign(catalog, IncentiveDefinitionTests.Definition(), M(2026, 1), M(2026, 4));

        assignment.EnsureDeletable(NoClosedMonths).IsSuccess.ShouldBeTrue();
        assignment.EnsureDeletable(Closed((2025, 12), (2026, 5))).IsSuccess.ShouldBeTrue();
        assignment.EnsureDeletable(Closed((2026, 3))).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveAssignmentPeriodClosed);
    }

    [Fact]
    public void IsCovered_FollowsTheRange()
    {
        var assignment = Assign(new Catalog(), IncentiveDefinitionTests.Definition(), M(2026, 3), M(2026, 5));

        assignment.IsCovered(M(2026, 2)).ShouldBeFalse();
        assignment.IsCovered(M(2026, 3)).ShouldBeTrue();
        assignment.IsCovered(M(2026, 5)).ShouldBeTrue();
        assignment.IsCovered(M(2026, 6)).ShouldBeFalse();
    }
}
