using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>IncentiveDefinition: parameter validation, completeness, approval and approval-drop rules (PAY-INCENTIVE-1). Pure domain.</summary>
public sealed class IncentiveDefinitionTests
{
    internal static readonly Guid TenantId = Guid.NewGuid();
    internal static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateTimeOffset Now = new(2026, 3, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    internal static IncentiveParameters CoverageParameters() => new()
    {
        Rate = IncentiveRate.Literal(1m),
        CoverageBase = IncentiveCoverageBase.MinimumWage,
        CapMultiplier = 1m,
        EmployerSgkRate = IncentiveRate.Literal(0.155m),
        CoversEmployerSgk = true,
        CoversEmployerUnemployment = true,
    };

    internal static IncentiveDefinitionData Data(
        IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount,
        IncentiveParameters? parameters = null,
        string name = "Test teşviki",
        string? lawReference = "Test Kanunu",
        string? declarationCode = "06111",
        int? maxMonths = null,
        DateOnly? validTo = null) =>
        new(name, lawReference, declarationCode, kind, parameters ?? DefaultParameters(kind), maxMonths, Jan, validTo);

    internal static IncentiveParameters DefaultParameters(IncentiveKind kind) => kind switch
    {
        IncentiveKind.EmployerShareCoverage => CoverageParameters(),
        IncentiveKind.DeclarationOnly => IncentiveParameters.Empty,
        _ => new IncentiveParameters { Rate = IncentiveRate.Literal(0.05m) },
    };

    internal static IncentiveDefinition Definition(
        IncentiveKind kind = IncentiveKind.EmployerSgkRateDiscount,
        string code = "TEST",
        int? maxMonths = null,
        string? declarationCode = "06111") =>
        IncentiveDefinition.Create(TenantId, code, Data(kind, maxMonths: maxMonths, declarationCode: declarationCode)).Value;

    private static string FieldOf(Error error) => (string)error.Args!["field"]!;

    [Theory]
    [InlineData("ABC")]
    [InlineData("A1")]
    [InlineData("SGK_6111_TASLAK")]
    public void Code_AcceptsTheDocumentedPattern(string code)
    {
        IncentiveDefinition.Create(TenantId, code, Data()).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("abc")]
    [InlineData("1ABC")]
    [InlineData("A-B")]
    [InlineData("ÇAY")]
    public void Code_RejectsAnythingElse(string code)
    {
        IncentiveDefinition.Create(TenantId, code, Data()).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveCodeInvalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsRequired(string name)
    {
        IncentiveDefinition.Create(TenantId, "TEST", Data(name: name)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveNameInvalid);
    }

    [Fact]
    public void NameAndLawReference_AreLengthBounded()
    {
        IncentiveDefinition.Create(TenantId, "TEST", Data(name: new string('a', 100))).IsSuccess.ShouldBeTrue();
        IncentiveDefinition.Create(TenantId, "TEST", Data(name: new string('a', 101))).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveNameInvalid);
        IncentiveDefinition.Create(TenantId, "TEST", Data(lawReference: new string('a', 200))).IsSuccess.ShouldBeTrue();
        IncentiveDefinition.Create(TenantId, "TEST", Data(lawReference: new string('a', 201))).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveNameInvalid);
    }

    [Theory]
    [InlineData("05510", true)]
    [InlineData("1", true)]
    [InlineData("1234567890", true)]
    [InlineData("12345678901", false)]
    [InlineData("05A10", false)]
    [InlineData("05 10", false)]
    public void DeclarationCode_IsDigitsOnlyUpToTen(string declarationCode, bool valid)
    {
        var result = IncentiveDefinition.Create(TenantId, "TEST", Data(declarationCode: declarationCode));

        result.IsSuccess.ShouldBe(valid);
        if (!valid)
        {
            result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveDeclarationCodeInvalid);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(120, true)]
    [InlineData(0, false)]
    [InlineData(121, false)]
    public void MaxMonths_MustBeBetween1And120(int maxMonths, bool valid)
    {
        var result = IncentiveDefinition.Create(TenantId, "TEST", Data(maxMonths: maxMonths));

        result.IsSuccess.ShouldBe(valid);
        if (!valid)
        {
            result.Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
            FieldOf(result.Error).ShouldBe("maxMonths");
        }
    }

    [Fact]
    public void ValidTo_BeforeValidFrom_IsRejected()
    {
        IncentiveDefinition.Create(TenantId, "TEST", Data(validTo: Jan.AddDays(-1))).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveDateRangeInvalid);
    }

    [Theory]
    [InlineData(IncentiveKind.EmployerSgkRateDiscount, IncentiveCategory.Sgk)]
    [InlineData(IncentiveKind.EmployerShareCoverage, IncentiveCategory.Sgk)]
    [InlineData(IncentiveKind.DeclarationOnly, IncentiveCategory.Sgk)]
    [InlineData(IncentiveKind.IncomeTaxWithholdingRelief, IncentiveCategory.Tax)]
    public void Category_FollowsTheKind(IncentiveKind kind, IncentiveCategory category)
    {
        Definition(kind).Category.ShouldBe(category);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.05)]
    [InlineData(1)]
    public void RateValue_InsideZeroExclusiveToOne_IsAccepted(double rate)
    {
        var parameters = new IncentiveParameters { Rate = IncentiveRate.Literal((decimal)rate) };

        IncentiveDefinition.Create(TenantId, "TEST", Data(parameters: parameters)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void RateValue_OutsideRange_IsRejected(double rate)
    {
        var parameters = new IncentiveParameters { Rate = IncentiveRate.Literal((decimal)rate) };

        var error = IncentiveDefinition.Create(TenantId, "TEST", Data(parameters: parameters)).Error;

        error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
        FieldOf(error).ShouldBe("rate");
    }

    [Fact]
    public void Rate_WithBothValueAndLegislationKey_IsRejected()
    {
        var parameters = new IncentiveParameters { Rate = new IncentiveRate { Value = 0.05m, LegislationKey = "Payroll.Sgk.EmployerDiscountRate" } };

        IncentiveDefinition.Create(TenantId, "TEST", Data(parameters: parameters)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
    }

    [Theory]
    [InlineData("Sgk.EmployerDiscountRate")]
    [InlineData("payroll.Sgk.EmployerDiscountRate")]
    [InlineData("Payroll.")]
    public void LegislationKey_WithoutPayrollPrefix_IsRejected(string key)
    {
        var parameters = new IncentiveParameters { Rate = IncentiveRate.Key(key) };

        IncentiveDefinition.Create(TenantId, "TEST", Data(parameters: parameters)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveLegislationKeyInvalid);
    }

    [Fact]
    public void LegislationKey_IsLimitedTo100Characters()
    {
        var atLimit = "Payroll." + new string('a', 92);
        var overLimit = atLimit + "a";

        IncentiveDefinition.Create(TenantId, "TEST", Data(parameters: new IncentiveParameters { Rate = IncentiveRate.Key(atLimit) })).IsSuccess.ShouldBeTrue();
        IncentiveDefinition.Create(TenantId, "TEST", Data(parameters: new IncentiveParameters { Rate = IncentiveRate.Key(overLimit) })).Error.Code
            .ShouldBe(PayrollDomainErrors.IncentiveLegislationKeyInvalid);
    }

    [Fact]
    public void EmployerSgkRate_FollowsTheSameRules()
    {
        var badValue = CoverageParameters() with { EmployerSgkRate = IncentiveRate.Literal(1.5m) };
        var badKey = CoverageParameters() with { EmployerSgkRate = IncentiveRate.Key("Other.Key") };

        FieldOf(IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.EmployerShareCoverage, badValue)).Error).ShouldBe("employerSgkRate");
        IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.EmployerShareCoverage, badKey)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveLegislationKeyInvalid);
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(10.01, false)]
    public void CapMultiplier_MustBeBetweenZeroExclusiveAndTen(double cap, bool valid)
    {
        var parameters = CoverageParameters() with { CapMultiplier = (decimal)cap };

        var result = IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.EmployerShareCoverage, parameters));

        result.IsSuccess.ShouldBe(valid);
        if (!valid)
        {
            FieldOf(result.Error).ShouldBe("capMultiplier");
        }
    }

    [Theory]
    [InlineData(IncentiveKind.EmployerSgkRateDiscount)]
    [InlineData(IncentiveKind.IncomeTaxWithholdingRelief)]
    public void RateOnlyKinds_RejectCoverageFields(IncentiveKind kind)
    {
        var parameters = new IncentiveParameters { Rate = IncentiveRate.Literal(0.5m), CoversEmployerSgk = true };

        IncentiveDefinition.Create(TenantId, "TEST", Data(kind, parameters)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
    }

    [Fact]
    public void DeclarationOnly_RejectsAnyParameter()
    {
        var withRate = new IncentiveParameters { Rate = IncentiveRate.Literal(0.5m) };
        var withCoverage = new IncentiveParameters { CoversEmployeeSgk = true };

        IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.DeclarationOnly, withRate)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
        IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.DeclarationOnly, withCoverage)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
        IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.DeclarationOnly)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Draft_WithMissingParameters_CanBeSavedButIsNotComplete()
    {
        var definition = IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.EmployerShareCoverage, IncentiveParameters.Empty)).Value;

        definition.IsComplete.ShouldBeFalse();
        definition.IsSmmmApproved.ShouldBeFalse();
    }

    [Theory]
    [InlineData(IncentiveKind.EmployerSgkRateDiscount)]
    [InlineData(IncentiveKind.EmployerShareCoverage)]
    [InlineData(IncentiveKind.IncomeTaxWithholdingRelief)]
    [InlineData(IncentiveKind.DeclarationOnly)]
    public void CompleteParameters_MakeEachKindComplete(IncentiveKind kind)
    {
        Definition(kind).IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void SgkCategory_WithoutDeclarationCode_IsNotComplete_ButTaxCategoryIs()
    {
        Definition(IncentiveKind.EmployerSgkRateDiscount, declarationCode: null).IsComplete.ShouldBeFalse();
        Definition(IncentiveKind.DeclarationOnly, declarationCode: null).IsComplete.ShouldBeFalse();
        Definition(IncentiveKind.IncomeTaxWithholdingRelief, declarationCode: null).IsComplete.ShouldBeTrue();
    }

    [Theory]
    [InlineData(IncentiveKind.EmployerSgkRateDiscount)]
    [InlineData(IncentiveKind.IncomeTaxWithholdingRelief)]
    public void RateKinds_WithoutRate_AreIncomplete(IncentiveKind kind)
    {
        var definition = IncentiveDefinition.Create(TenantId, "TEST", Data(kind, IncentiveParameters.Empty)).Value;

        definition.Approve(UserId, Now, null).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersIncomplete);
        FieldOf(definition.Approve(UserId, Now, null).Error).ShouldBe("rate");
    }

    [Theory]
    [InlineData("coverageBase")]
    [InlineData("capMultiplier")]
    [InlineData("coversEmployerSgk")]
    [InlineData("employerSgkRate")]
    public void Coverage_ReportsTheFirstMissingField(string missing)
    {
        var parameters = missing switch
        {
            "coverageBase" => CoverageParameters() with { CoverageBase = null },
            "capMultiplier" => CoverageParameters() with { CapMultiplier = null },
            "coversEmployerSgk" => CoverageParameters() with { CoversEmployerSgk = false, CoversEmployerUnemployment = false },
            _ => CoverageParameters() with { EmployerSgkRate = null },
        };
        var definition = IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.EmployerShareCoverage, parameters)).Value;

        var error = definition.Approve(UserId, Now, null).Error;

        error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersIncomplete);
        FieldOf(error).ShouldBe(missing);
    }

    [Fact]
    public void Coverage_WithActualSgkBase_DoesNotNeedACapMultiplier_AndEmployeeOnlyNeedsNoEmployerRate()
    {
        var parameters = new IncentiveParameters
        {
            Rate = IncentiveRate.Literal(1m),
            CoverageBase = IncentiveCoverageBase.ActualSgkBase,
            CoversEmployeeSgk = true,
        };

        Definition(IncentiveKind.EmployerShareCoverage).IsComplete.ShouldBeTrue();
        IncentiveDefinition.Create(TenantId, "TEST", Data(IncentiveKind.EmployerShareCoverage, parameters)).Value.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void Approve_RequiresCompleteness_AndDeclarationCodeInSgkCategory()
    {
        var noCode = Definition(declarationCode: null);

        noCode.Approve(UserId, Now, null).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveDeclarationCodeRequired);
        noCode.IsSmmmApproved.ShouldBeFalse();
    }

    [Fact]
    public void Approve_RecordsWhoWhenAndNote()
    {
        var definition = Definition();

        definition.Approve(UserId, Now, "  SMMM kontrol etti  ").IsSuccess.ShouldBeTrue();

        definition.IsSmmmApproved.ShouldBeTrue();
        definition.ApprovedByUserId.ShouldBe(UserId);
        definition.ApprovedAt.ShouldBe(Now);
        definition.ApprovalNote.ShouldBe("SMMM kontrol etti");
    }

    [Fact]
    public void Approve_RejectsANoteOver500Characters()
    {
        Definition().Approve(UserId, Now, new string('n', 501)).Error.Code.ShouldBe(PayrollDomainErrors.IncentiveParametersInvalid);
    }

    [Fact]
    public void RevokeApproval_ClearsTheApprovalAndKeepsTheNote()
    {
        var definition = Definition();
        definition.Approve(UserId, Now, null);

        definition.RevokeApproval("Oran değişti").IsSuccess.ShouldBeTrue();

        definition.IsSmmmApproved.ShouldBeFalse();
        definition.ApprovedAt.ShouldBeNull();
        definition.ApprovedByUserId.ShouldBeNull();
        definition.ApprovalNote.ShouldBe("Oran değişti");
    }

    public static TheoryData<string> CalculationRelevantChanges() => new() { "kind", "parameters", "declarationCode", "maxMonths", "validFrom", "validTo" };

    [Theory]
    [MemberData(nameof(CalculationRelevantChanges))]
    public void Update_OfACalculationField_DropsTheApproval(string field)
    {
        var definition = Definition(IncentiveKind.EmployerSgkRateDiscount, maxMonths: 12);
        definition.Approve(UserId, Now, "ok");
        var data = Data(maxMonths: 12);
        var changed = field switch
        {
            "kind" => data with { Kind = IncentiveKind.DeclarationOnly, Parameters = IncentiveParameters.Empty },
            "parameters" => data with { Parameters = new IncentiveParameters { Rate = IncentiveRate.Literal(0.06m) } },
            "declarationCode" => data with { DeclarationCode = "06112" },
            "maxMonths" => data with { MaxMonths = 24 },
            "validFrom" => data with { ValidFrom = Jan.AddMonths(1) },
            _ => data with { ValidTo = Jan.AddYears(1) },
        };

        definition.Update(changed).IsSuccess.ShouldBeTrue();

        definition.IsSmmmApproved.ShouldBeFalse();
        definition.ApprovedAt.ShouldBeNull();
        definition.ApprovedByUserId.ShouldBeNull();
    }

    [Fact]
    public void Update_OfNameOrLawReferenceOnly_KeepsTheApproval()
    {
        var definition = Definition();
        definition.Approve(UserId, Now, "ok");

        definition.Update(Data(name: "Yeni ad", lawReference: "Yeni dayanak")).IsSuccess.ShouldBeTrue();

        definition.Name.ShouldBe("Yeni ad");
        definition.LawReference.ShouldBe("Yeni dayanak");
        definition.IsSmmmApproved.ShouldBeTrue();
        definition.ApprovedByUserId.ShouldBe(UserId);
    }

    [Fact]
    public void Update_WithIdenticalCalculationFields_KeepsTheApproval()
    {
        var definition = Definition();
        definition.Approve(UserId, Now, "ok");

        definition.Update(Data()).IsSuccess.ShouldBeTrue();

        definition.IsSmmmApproved.ShouldBeTrue();
    }

    [Fact]
    public void Update_Invalid_ChangesNothingAndKeepsTheApproval()
    {
        var definition = Definition();
        definition.Approve(UserId, Now, "ok");

        definition.Update(Data(parameters: new IncentiveParameters { Rate = IncentiveRate.Literal(5m) })).IsFailure.ShouldBeTrue();

        definition.IsSmmmApproved.ShouldBeTrue();
        definition.Parameters.Rate!.Value.ShouldBe(0.05m);
    }

    [Fact]
    public void IsEffectiveInMonth_RequiresActiveAndOverlappingDates()
    {
        var definition = IncentiveDefinition.Create(TenantId, "TEST", Data(validTo: new DateOnly(2026, 6, 15))).Value;

        definition.IsEffectiveInMonth(new DateOnly(2025, 12, 1)).ShouldBeFalse();
        definition.IsEffectiveInMonth(Jan).ShouldBeTrue();
        definition.IsEffectiveInMonth(new DateOnly(2026, 6, 1)).ShouldBeTrue();
        definition.IsEffectiveInMonth(new DateOnly(2026, 7, 1)).ShouldBeFalse();

        definition.Deactivate();
        definition.IsEffectiveInMonth(Jan).ShouldBeFalse();
        definition.Activate();
        definition.IsEffectiveInMonth(Jan).ShouldBeTrue();
    }

    [Fact]
    public void LegislationKeys_ListsEveryReferencedKey()
    {
        var parameters = CoverageParameters() with
        {
            Rate = IncentiveRate.Key("Payroll.A"),
            EmployerSgkRate = IncentiveRate.Key("Payroll.B"),
        };

        parameters.LegislationKeys.ShouldBe(["Payroll.A", "Payroll.B"]);
        CoverageParameters().LegislationKeys.ShouldBeEmpty();
    }
}
