using System.Globalization;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.PayItems.Formulas;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Formulas;

/// <summary>PAY-FORMULA-1: the pure, decimal-only pay-item formula language (plan: "Formül dili").</summary>
public sealed class PayrollFormulaTests
{
    private static readonly IReadOnlyDictionary<string, decimal> Vars = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["brut"] = 30_000m,
        ["aylik_ucret"] = 30_000m,
        ["net_hedef"] = 0m,
        ["gunluk_ucret"] = 1_000m,
        ["saatlik_ucret"] = 133.33m,
        ["calisilan_gun"] = 26m,
        ["ucretli_gun"] = 30m,
        ["takvim_gunu"] = 31m,
        ["eksik_gun"] = 0m,
        ["kidem_yili"] = 6m,
        ["asgari_ucret"] = 20_000m,
        ["fazla_mesai_saati"] = 10m,
        ["ay"] = 8m,
        ["deger"] = 500m,
    };

    private static Result<decimal> Run(string text) =>
        PayrollFormula.Parse(text).Bind(f => f.Evaluate(Vars));

    private static decimal Ok(string text)
    {
        var result = Run(text);
        result.IsSuccess.ShouldBeTrue($"'{text}' failed with {result.Error.Code}");
        return result.Value;
    }

    private static Error Fail(string text)
    {
        var result = Run(text);
        result.IsFailure.ShouldBeTrue($"'{text}' unexpectedly evaluated");
        return result.Error;
    }

    private static int Position(Error error) => (int)error.Args!["position"]!;

    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("10-4-3", 3)]
    [InlineData("2*3*4", 24)]
    [InlineData("12/4/3", 1)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("-5+2", -3)]
    [InlineData("--5", 5)]
    [InlineData("2*-3", -6)]
    [InlineData("1.5*2", 3)]
    [InlineData("  7  ", 7)]
    [InlineData("deger*2", 1000)]
    [InlineData("DEGER * 2", 1000)]
    [InlineData("brut/30", 1000)]
    [InlineData("0.1+0.2", 0.3)]
    public void Arithmetic_FollowsPrecedenceAndAssociativity(string formula, double expected) =>
        Ok(formula).ShouldBe((decimal)expected);

    [Theory]
    [InlineData("1<2", 1)]
    [InlineData("2<=2", 1)]
    [InlineData("3>4", 0)]
    [InlineData("4>=5", 0)]
    [InlineData("2=2", 1)]
    [InlineData("2!=2", 0)]
    [InlineData("1+1=2", 1)]
    [InlineData("1 ve 1", 1)]
    [InlineData("1 and 0", 0)]
    [InlineData("0 veya 1", 1)]
    [InlineData("0 or 0", 0)]
    [InlineData("degil 0", 1)]
    [InlineData("not 5", 0)]
    [InlineData("degil 1 ve 0", 0)] // (not 1) and 0
    [InlineData("degil 2 > 3", 1)] // not (2 > 3): not binds looser than comparison
    [InlineData("1 veya 0 ve 0", 1)] // and binds tighter than or
    [InlineData("1 < 2 ve 2 < 3", 1)]
    public void ComparisonAndLogic_YieldOneOrZero(string formula, int expected) =>
        Ok(formula).ShouldBe(expected);

    [Theory]
    [InlineData("min(3;1;2)", 1)]
    [InlineData("min(3,1)", 1)]
    [InlineData("max(3;1;2)", 3)]
    [InlineData("max(-1,-2)", -1)]
    [InlineData("yuvarla(2.5)", 3)]
    [InlineData("round(-2.5)", -3)]
    [InlineData("yuvarla(1.005; 2)", 1.01)]
    [InlineData("round(-1.005, 2)", -1.01)]
    [InlineData("yuvarla(3.14159; 4)", 3.1416)]
    [InlineData("asagi(2.9)", 2)]
    [InlineData("floor(-2.1)", -3)]
    [InlineData("yukari(2.1)", 3)]
    [InlineData("ceil(-2.9)", -2)]
    [InlineData("mutlak(-4)", 4)]
    [InlineData("abs(4)", 4)]
    [InlineData("eger(1;10;20)", 10)]
    [InlineData("if(0,10,20)", 20)]
    [InlineData("EGER(deger>400; 1; 2)", 1)]
    [InlineData("min(max(1;2);5)", 2)]
    [InlineData("yuvarla(brut*0.0759;2)", 2277)]
    public void Functions_TurkishAndEnglishNames(string formula, double expected) =>
        Ok(formula).ShouldBe((decimal)expected);

    [Fact]
    public void Rounding_IsHalfAwayFromZero_NotBankers()
    {
        Ok("yuvarla(0.5)").ShouldBe(1m);
        Ok("yuvarla(1.5)").ShouldBe(2m);
        Ok("yuvarla(2.5)").ShouldBe(3m);
        Ok("yuvarla(-0.5)").ShouldBe(-1m);
        Ok("yuvarla(2.345;2)").ShouldBe(2.35m);
    }

    [Fact]
    public void If_EvaluatesOnlyTheChosenBranch()
    {
        Ok("eger(1; 5; 1/0)").ShouldBe(5m);
        Ok("eger(0; 1/0; 7)").ShouldBe(7m);
        Fail("eger(1; 1/0; 7)").Code.ShouldBe(PayrollDomainErrors.FormulaDivisionByZero);
    }

    [Fact]
    public void LogicalOperators_ShortCircuit()
    {
        Ok("0 ve 1/0").ShouldBe(0m);
        Ok("1 veya 1/0").ShouldBe(1m);
        Ok("deger != 0 ve 1000/deger > 1").ShouldBe(1m);
    }

    [Fact]
    public void RealisticFormulas_Evaluate()
    {
        // Meal-like allowance: paid per worked day, capped.
        Ok("min(calisilan_gun * 100; 2000)").ShouldBe(2000m);
        // Seniority bonus: 1% of the minimum wage per year, only from year 5.
        Ok("eger(kidem_yili >= 5; asgari_ucret * 0.01 * kidem_yili; 0)").ShouldBe(1200m);
        Ok("deger").ShouldBe(500m);
    }

    [Fact]
    public void DivisionByZero_IsAResultError_WithOperatorPosition()
    {
        var error = Fail("1 + 5/0");
        error.Code.ShouldBe(PayrollDomainErrors.FormulaDivisionByZero);
        Position(error).ShouldBe(6);
        Fail("1/(deger-500)").Code.ShouldBe(PayrollDomainErrors.FormulaDivisionByZero);
    }

    [Theory]
    [InlineData("1000000000 * 2")]
    [InlineData("999999999 + 999999999")]
    [InlineData("99999999999999999999")] // 20 digits, over the magnitude limit at parse
    [InlineData("1000000000.01")]
    [InlineData("500000 * 500000")]
    [InlineData("-500000 * 500000")]
    [InlineData("brut * brut * brut")]
    public void Overflow_IsAResultError(string formula) =>
        Fail(formula).Code.ShouldBe(PayrollDomainErrors.FormulaOverflow);

    [Fact]
    public void Magnitude_LimitItselfIsAllowed() => Ok("1000000000").ShouldBe(1_000_000_000m);

    [Fact]
    public void DecimalComma_IsRejectedWithItsOwnCode()
    {
        var error = Fail("1,5 * 2");
        error.Code.ShouldBe(PayrollDomainErrors.FormulaDecimalComma);
        Position(error).ShouldBe(2);
        Fail("(2,5)").Code.ShouldBe(PayrollDomainErrors.FormulaDecimalComma);
        Fail("deger * 0,5").Code.ShouldBe(PayrollDomainErrors.FormulaDecimalComma);
    }

    [Fact]
    public void ArgumentSeparator_CommaIsStillValidInsideCalls() => Ok("max(1,5)").ShouldBe(5m);

    [Fact]
    public void EmptyFormula_IsRejected()
    {
        PayrollFormula.Parse(null).Error.Code.ShouldBe(PayrollDomainErrors.FormulaEmpty);
        PayrollFormula.Parse("").Error.Code.ShouldBe(PayrollDomainErrors.FormulaEmpty);
        PayrollFormula.Parse("   ").Error.Code.ShouldBe(PayrollDomainErrors.FormulaEmpty);
    }

    [Theory]
    [InlineData("1 +", 4)]
    [InlineData("* 2", 1)]
    [InlineData("(1 + 2", 7)]
    [InlineData("1 + 2)", 6)]
    [InlineData("1 2", 3)]
    [InlineData("1 $ 2", 3)]
    [InlineData("max(1;)", 7)]
    [InlineData("max(1 2)", 7)]
    [InlineData("1 == 2", 4)]
    [InlineData("1 <> 2", 4)]
    [InlineData(".5", 1)]
    [InlineData("5.", 2)]
    [InlineData("ğ", 1)]
    public void SyntaxErrors_CarryTheOffendingPosition(string formula, int position)
    {
        var error = PayrollFormula.Parse(formula).Error;
        error.Code.ShouldBe(PayrollDomainErrors.FormulaSyntax);
        Position(error).ShouldBe(position);
    }

    [Fact]
    public void UnknownVariable_IsRejectedAtParseWithPositionAndName()
    {
        var result = PayrollFormula.Parse("1 + bilinmeyen");
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.FormulaUnknownVariable);
        Position(result.Error).ShouldBe(5);
        result.Error.Args!["name"].ShouldBe("bilinmeyen");
    }

    [Fact]
    public void FunctionNameUsedAsVariable_IsUnknownVariable() =>
        PayrollFormula.Parse("min + 1").Error.Code.ShouldBe(PayrollDomainErrors.FormulaUnknownVariable);

    [Fact]
    public void UnknownFunction_IsRejectedAtParseWithPositionAndName()
    {
        var result = PayrollFormula.Parse("1 + sqrt(4)");
        result.Error.Code.ShouldBe(PayrollDomainErrors.FormulaUnknownFunction);
        Position(result.Error).ShouldBe(5);
        result.Error.Args!["name"].ShouldBe("sqrt");
    }

    [Theory]
    [InlineData("min(1)", "min")]
    [InlineData("max()", "max")]
    [InlineData("yuvarla()", "yuvarla")]
    [InlineData("yuvarla(1;2;3)", "yuvarla")]
    [InlineData("asagi(1;2)", "asagi")]
    [InlineData("round()", "yuvarla")]
    [InlineData("mutlak(1;2)", "mutlak")]
    [InlineData("eger(1;2)", "eger")]
    [InlineData("if(1;2;3;4)", "eger")]
    public void WrongArgumentCount_IsRejectedAtParse(string formula, string canonicalName)
    {
        var error = PayrollFormula.Parse(formula).Error;
        error.Code.ShouldBe(PayrollDomainErrors.FormulaArgumentCount);
        Position(error).ShouldBe(1);
        error.Args!["name"].ShouldBe(canonicalName);
    }

    [Fact]
    public void ArgumentCount_ReportsMaxWhenBounded()
    {
        PayrollFormula.Parse("eger(1;2)").Error.Args!["max"].ShouldBe(3);
        PayrollFormula.Parse("min(1)").Error.Args!.ContainsKey("max").ShouldBeFalse();
    }

    [Theory]
    [InlineData("yuvarla(1.5; 5)")]
    [InlineData("yuvarla(1.5; -1)")]
    [InlineData("yuvarla(1.5; 1.5)")]
    public void RoundDigitsOutOfRange_IsAResultError(string formula) =>
        Fail(formula).Code.ShouldBe(PayrollDomainErrors.FormulaSyntax);

    [Fact]
    public void UsedVariables_AreDistinctSortedAndLowerCase()
    {
        var formula = PayrollFormula.Parse("min(DEGER; brut * 0.1) + deger + kidem_yili").Value;
        formula.UsedVariables.ShouldBe(["brut", "deger", "kidem_yili"]);
        PayrollFormula.Parse("1 + 2").Value.UsedVariables.ShouldBeEmpty();
    }

    [Fact]
    public void Text_KeepsTheOriginal() => PayrollFormula.Parse(" MIN(1;2) ").Value.Text.ShouldBe(" MIN(1;2) ");

    [Fact]
    public void Evaluate_MissingVariableValue_IsUnknownVariableResult()
    {
        var formula = PayrollFormula.Parse("brut + 1").Value;
        var result = formula.Evaluate(new Dictionary<string, decimal>());
        result.Error.Code.ShouldBe(PayrollDomainErrors.FormulaUnknownVariable);
    }

    [Fact]
    public void VariableNames_AreTheFixedPlanList()
    {
        PayItemVariableNames.All.ShouldBe(
        [
            "brut", "aylik_ucret", "net_hedef", "gunluk_ucret", "saatlik_ucret", "calisilan_gun", "ucretli_gun",
            "takvim_gunu", "eksik_gun", "kidem_yili", "asgari_ucret", "fazla_mesai_saati", "ay", "deger",
        ]);
        foreach (var name in PayItemVariableNames.All)
        {
            PayItemVariableNames.IsAllowed(name).ShouldBeTrue();
            Ok(name).ShouldBe(Vars[name]);
        }

        PayItemVariableNames.IsAllowed("maas").ShouldBeFalse();
        PayItemVariableNames.IsAllowed("BRUT").ShouldBeFalse();
    }

    [Fact]
    public void FunctionCatalog_ListsTurkishNamesWithEnglishAliases()
    {
        PayrollFormula.Functions.Select(f => f.Name).ShouldBe(["min", "max", "yuvarla", "asagi", "yukari", "mutlak", "eger"]);
        var round = PayrollFormula.Functions.Single(f => f.Name == "yuvarla");
        round.Aliases.ShouldBe(["round"]);
        (round.MinArgs, round.MaxArgs).ShouldBe((1, 2));
        PayrollFormula.Functions.Single(f => f.Name == "min").MaxArgs.ShouldBeNull();
    }

    // --- limits ---

    [Fact]
    public void Limits_AreTheDocumentedNamedConstants()
    {
        FormulaLimits.MaxTextLength.ShouldBe(500);
        FormulaLimits.MaxTokens.ShouldBe(200);
        FormulaLimits.MaxDepth.ShouldBe(20);
        FormulaLimits.MaxIdentifierLength.ShouldBe(40);
        FormulaLimits.MaxNumberLength.ShouldBe(20);
    }

    [Fact]
    public void Length_AtLimitPasses_AboveLimitFails()
    {
        var atLimit = "1" + new string(' ', FormulaLimits.MaxTextLength - 1);
        PayrollFormula.Parse(atLimit).IsSuccess.ShouldBeTrue();

        var error = PayrollFormula.Parse(atLimit + " ").Error;
        error.Code.ShouldBe(PayrollDomainErrors.FormulaTooLong);
        error.Args!["max"].ShouldBe(FormulaLimits.MaxTextLength);
    }

    [Fact]
    public void TokenCount_AtLimitPasses_AboveLimitFails()
    {
        // n numbers and n-1 plus signs plus the end marker.
        var fits = string.Join("+", Enumerable.Repeat("1", 100)); // 199 tokens
        PayrollFormula.Parse(fits).IsSuccess.ShouldBeTrue();

        var tooMany = string.Join("+", Enumerable.Repeat("1", 101)); // 201 tokens
        var error = PayrollFormula.Parse(tooMany).Error;
        error.Code.ShouldBe(PayrollDomainErrors.FormulaTooLong);
        error.Args!["max"].ShouldBe(FormulaLimits.MaxTokens);
    }

    [Fact]
    public void Depth_AtLimitPasses_AboveLimitFails()
    {
        string Nested(int levels) => new string('(', levels) + "1" + new string(')', levels);

        PayrollFormula.Parse(Nested(FormulaLimits.MaxDepth - 1)).IsSuccess.ShouldBeTrue();

        var error = PayrollFormula.Parse(Nested(FormulaLimits.MaxDepth + 5)).Error;
        error.Code.ShouldBe(PayrollDomainErrors.FormulaTooDeep);
        error.Args!["max"].ShouldBe(FormulaLimits.MaxDepth);
        Fail(string.Concat(Enumerable.Repeat("-", 50)) + "1").Code.ShouldBe(PayrollDomainErrors.FormulaTooDeep);
        Fail(string.Concat(Enumerable.Repeat("degil ", 50)) + "1").Code.ShouldBe(PayrollDomainErrors.FormulaTooDeep);
    }

    [Fact]
    public void IdentifierLength_AboveLimitFails()
    {
        var error = PayrollFormula.Parse(new string('a', FormulaLimits.MaxIdentifierLength + 1)).Error;
        error.Code.ShouldBe(PayrollDomainErrors.FormulaTooLong);
        error.Args!["max"].ShouldBe(FormulaLimits.MaxIdentifierLength);
        Position(error).ShouldBe(1);

        // At the limit the length check passes and the name is simply unknown.
        PayrollFormula.Parse(new string('a', FormulaLimits.MaxIdentifierLength)).Error.Code
            .ShouldBe(PayrollDomainErrors.FormulaUnknownVariable);
    }

    [Fact]
    public void NumberLength_AboveLimitFails()
    {
        var error = PayrollFormula.Parse("1 + " + new string('1', FormulaLimits.MaxNumberLength + 1)).Error;
        error.Code.ShouldBe(PayrollDomainErrors.FormulaTooLong);
        error.Args!["max"].ShouldBe(FormulaLimits.MaxNumberLength);
        Position(error).ShouldBe(5);

        Ok("0." + new string('0', 17) + "1").ShouldBe(0.000000000000000001m); // exactly 20 characters
    }

    // --- culture independence ---

    [Fact]
    public void Results_AreIdenticalInTrTrAndEnUs()
    {
        string[] formulas =
        [
            "yuvarla(brut * 0.0759; 2)",
            "eger(kidem_yili >= 5; asgari_ucret * 0.01 * kidem_yili; 0)",
            "1.5 + 2.25 * 3",
            "min(1.1; 2.2) + max(3.3; 4.4)",
            "DEGER * 0.5",
            "IF(KIDEM_YILI > 1; 1; 0)",
        ];
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var formula in formulas)
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
                var tr = Run(formula);
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en-US");
                var en = Run(formula);

                tr.IsSuccess.ShouldBeTrue(formula);
                en.Value.ShouldBe(tr.Value, formula);
            }

            // "I" / "i" folding must not depend on the Turkish culture: DEĞER-like names stay ASCII-invariant.
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            Ok("IF(1;2;3)").ShouldBe(2m);
            Ok("MIN(1;2)").ShouldBe(1m);
            Ok("1.5").ShouldBe(1.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    // --- robustness ---

    [Fact]
    public void RandomInput_NeverThrows()
    {
        // Fixed seed: reproducible. Mix of grammar fragments and raw noise exercises lexer, parser and evaluator.
        string[] fragments =
        [
            "1", "0", "2.5", "999999999", "brut", "deger", "kidem_yili", "x", "(", ")", ",", ";", "+", "-", "*", "/", "<", "<=", ">", ">=", "=", "!=",
            "ve", "veya", "degil", "and", "or", "not", "min(", "max(", "yuvarla(", "round(", "eger(", "if(", "asagi(", "mutlak(", " ", "1,5", ".", "$", "ğ",
            "999999999999999999999",
        ];
        var random = new Random(20261013);
        var vars = (IReadOnlyDictionary<string, decimal>)Vars;
        for (var i = 0; i < 10_000; i++)
        {
            var text = i % 4 == 0 ? RandomNoise(random) : RandomFragments(random, fragments);
            var exception = Record.Exception(() =>
            {
                var parsed = PayrollFormula.Parse(text);
                if (parsed.IsSuccess)
                {
                    parsed.Value.Evaluate(vars);
                }
                else
                {
                    parsed.Error.Code.ShouldStartWith("payroll.formula.");
                }
            });
            exception.ShouldBeNull($"input #{i}: '{text}'");
        }
    }

    private static string RandomFragments(Random random, string[] fragments)
    {
        var count = random.Next(1, 40);
        return string.Concat(Enumerable.Range(0, count).Select(_ => fragments[random.Next(fragments.Length)]));
    }

    private static string RandomNoise(Random random)
    {
        var length = random.Next(0, 600);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)random.Next(0, 0x2FF);
        }

        return new string(chars);
    }
}
