using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Terminations;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>SgkExitCodeRules (senseik-006/F-01): the single source of truth both TerminationReasonCommands and TerminateEmployeeHandler check against.</summary>
public sealed class SgkExitCodeRulesTests
{
    [Theory]
    [InlineData(TerminationType.ValidCause_Art18, "04")]
    [InlineData(TerminationType.MutualRescission, "22")]
    [InlineData(TerminationType.Resignation, "03")]
    [InlineData(TerminationType.FixedTermExpiry, "05")]
    public void AllowedCodes_SingletonFamilies_ReturnExactlyOneCode(TerminationType type, string expectedOnlyCode)
    {
        var allowed = SgkExitCodeRules.AllowedCodes(type);
        allowed.ShouldNotBeNull();
        allowed.ShouldBe([expectedOnlyCode]);
    }

    [Fact]
    public void AllowedCodes_EmployeeJustCause_ReturnsTheThreeArt24Codes() =>
        SgkExitCodeRules.AllowedCodes(TerminationType.EmployeeJustCause_Art24).ShouldBe(["23", "24", "25"], ignoreOrder: true);

    [Fact]
    public void AllowedCodes_EmployerJustCause_ReturnsTheThreeArt25Codes() =>
        SgkExitCodeRules.AllowedCodes(TerminationType.EmployerJustCause_Art25).ShouldBe(["26", "27", "28"], ignoreOrder: true);

    [Fact]
    public void AllowedCodes_Other_IsUnrestricted() =>
        SgkExitCodeRules.AllowedCodes(TerminationType.Other).ShouldBeNull();

    [Theory]
    [InlineData(TerminationType.ValidCause_Art18, "04", true)]
    [InlineData(TerminationType.ValidCause_Art18, "05", false)]
    [InlineData(TerminationType.Resignation, null, true, "no code given - nothing to check yet")]
    [InlineData(TerminationType.Other, "13", true, "Other imposes no family restriction")]
    public void IsCatalogConsistent_ChecksMembershipOnlyWhenACodeIsGiven(TerminationType type, string? code, bool expected, string because = "")
    {
        SgkExitCodeRules.IsCatalogConsistent(type, code).ShouldBe(expected, because);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsCatalogConsistent_LeavesAmbiguousFamilyCodeUnset_IsConsistent(string? blank) =>
        SgkExitCodeRules.IsCatalogConsistent(TerminationType.EmployeeJustCause_Art24, blank).ShouldBeTrue(
            "the system seed intentionally leaves md.24/md.25 without a default sub-code");

    [Fact]
    public void IsCatalogConsistent_NullType_ImposesNoFamily_ButCodeMustBeAKnownSgkCode()
    {
        SgkExitCodeRules.IsCatalogConsistent(null, "10").ShouldBeTrue();
        SgkExitCodeRules.IsCatalogConsistent(null, "99").ShouldBeFalse();
    }

    [Fact]
    public void IsCatalogConsistent_NullType_EmptyCodeIsFine() =>
        SgkExitCodeRules.IsCatalogConsistent(null, null).ShouldBeTrue();

    [Theory]
    [InlineData("23", true)]
    [InlineData("24", true)]
    [InlineData("25", true)]
    [InlineData("26", false)]
    [InlineData(null, false)]
    public void IsTerminationConsistent_AmbiguousFamily_RequiresAnExplicitMemberCode(string? code, bool expected) =>
        SgkExitCodeRules.IsTerminationConsistent(TerminationType.EmployeeJustCause_Art24, code).ShouldBe(expected);

    [Theory]
    [InlineData("04", true)]
    [InlineData(null, true, "a singleton family has a safe implied default")]
    [InlineData("05", false)]
    public void IsTerminationConsistent_SingletonFamily_AllowsAMissingCodeButNotAWrongOne(string? code, bool expected, string because = "")
    {
        SgkExitCodeRules.IsTerminationConsistent(TerminationType.ValidCause_Art18, code).ShouldBe(expected, because);
    }

    [Fact]
    public void IsTerminationConsistent_Other_AcceptsAnyCodeIncludingNull()
    {
        SgkExitCodeRules.IsTerminationConsistent(TerminationType.Other, null).ShouldBeTrue();
        SgkExitCodeRules.IsTerminationConsistent(TerminationType.Other, "22").ShouldBeTrue();
        SgkExitCodeRules.IsTerminationConsistent(TerminationType.Other, "13").ShouldBeTrue();
    }

    [Fact]
    public void IsTerminationConsistent_NullType_StillRequiresAPlausibleCode()
    {
        SgkExitCodeRules.IsTerminationConsistent(null, "10").ShouldBeTrue();
        SgkExitCodeRules.IsTerminationConsistent(null, null).ShouldBeTrue();
        SgkExitCodeRules.IsTerminationConsistent(null, "99").ShouldBeFalse();
    }

    /// <summary>
    /// Regression: reasons without a fixed type (Ölüm, Askerlik, Emeklilik, Evlilik, Deneme, Nakil) resolve to
    /// TerminationType.Other, which imposes no family - so an invented code like "99" used to be saved.
    /// </summary>
    [Theory]
    [InlineData("99", false)]
    [InlineData("00", false)]
    [InlineData("42", false)]
    [InlineData("1", false)]
    [InlineData("003", false)]
    [InlineData("ab", false)]
    [InlineData("1a", false)]
    [InlineData("01", true)]
    [InlineData("10", true)]
    [InlineData("41", true)]
    [InlineData(" 12 ", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public void IsPlausibleCode_AcceptsOnlyTwoDigitSgkRangeCodes(string? code, bool expected) =>
        SgkExitCodeRules.IsPlausibleCode(code).ShouldBe(expected);

    [Theory]
    [InlineData(TerminationType.Other, "99")]
    [InlineData(TerminationType.Other, "whatever")]
    [InlineData(TerminationType.Resignation, "3")]
    public void BothChecks_RejectMalformedOrUnknownCodes_EvenWhenTheFamilyIsUnrestricted(TerminationType type, string code)
    {
        SgkExitCodeRules.IsCatalogConsistent(type, code).ShouldBeFalse();
        SgkExitCodeRules.IsTerminationConsistent(type, code).ShouldBeFalse();
    }
}
