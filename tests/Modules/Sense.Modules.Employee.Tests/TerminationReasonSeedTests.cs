using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Terminations;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Verifies the system seed's SGK exit codes against SGK's official "İşten Ayrılış Nedenleri" table
/// (senseik-006/F-01, docs/handoffs/sgk-exit-codes.md §1) - the table this fixes was wrong for 10 of the 13 rows.
/// </summary>
public sealed class TerminationReasonSeedTests
{
    [Theory]
    [InlineData("Death", "10")]
    [InlineData("MilitaryService", "12")]
    [InlineData("Resignation", "03")]
    [InlineData("EmployerValidCause", "04")]
    [InlineData("MutualRescission", "22")]
    [InlineData("FixedTermExpiry", "05")]
    [InlineData("Retirement", "08")]
    [InlineData("MarriageFemale", "13")]
    [InlineData("ProbationTermination", "01")]
    [InlineData("GroupTransfer", "16")]
    [InlineData("Other", "22")]
    public void SystemDefinitions_HasTheVerifiedSgkExitCode(string code, string expectedSgkExitCode)
    {
        var row = TerminationReasonSeed.SystemDefinitions().Single(r => r.Code == code);
        row.DefaultSgkExitCode.ShouldBe(expectedSgkExitCode);
    }

    [Theory]
    [InlineData("EmployeeJustCause")]
    [InlineData("EmployerJustCause")]
    public void SystemDefinitions_LeavesAmbiguousFamiliesWithoutADefaultCode(string code)
    {
        var row = TerminationReasonSeed.SystemDefinitions().Single(r => r.Code == code);
        row.DefaultSgkExitCode.ShouldBeNull("md.24/md.25 each cover more than one SGK code - no single default is safe");
    }

    [Fact]
    public void SystemDefinitions_HasExactlyThirteenRows() =>
        TerminationReasonSeed.SystemDefinitions().Count.ShouldBe(13);

    /// <summary>Every non-null seeded code must itself be consistent with the type it defaults, so the seed never trips the catalog validator.</summary>
    [Fact]
    public void SystemDefinitions_AreAllInternallyConsistentWithSgkExitCodeRules()
    {
        foreach (var row in TerminationReasonSeed.SystemDefinitions())
        {
            SgkExitCodeRules.IsCatalogConsistent(row.DefaultTerminationType, row.DefaultSgkExitCode)
                .ShouldBeTrue($"{row.Code}: {row.DefaultTerminationType} / {row.DefaultSgkExitCode}");
        }
    }

    [Theory]
    [InlineData("EmployeeJustCause", TerminationType.EmployeeJustCause_Art24)]
    [InlineData("EmployerJustCause", TerminationType.EmployerJustCause_Art25)]
    [InlineData("Resignation", TerminationType.Resignation)]
    [InlineData("EmployerValidCause", TerminationType.ValidCause_Art18)]
    [InlineData("MutualRescission", TerminationType.MutualRescission)]
    [InlineData("FixedTermExpiry", TerminationType.FixedTermExpiry)]
    [InlineData("Other", TerminationType.Other)]
    public void SystemDefinitions_HasTheExpectedDefaultTerminationType(string code, TerminationType expectedType)
    {
        var row = TerminationReasonSeed.SystemDefinitions().Single(r => r.Code == code);
        row.DefaultTerminationType.ShouldBe(expectedType);
    }

    [Theory]
    [InlineData("Death")]
    [InlineData("MilitaryService")]
    [InlineData("Retirement")]
    [InlineData("MarriageFemale")]
    [InlineData("ProbationTermination")]
    [InlineData("GroupTransfer")]
    public void SystemDefinitions_LeavesReasonsWithoutAnObviousStatutoryTypeUnset(string code)
    {
        var row = TerminationReasonSeed.SystemDefinitions().Single(r => r.Code == code);
        row.DefaultTerminationType.ShouldBeNull();
    }
}
