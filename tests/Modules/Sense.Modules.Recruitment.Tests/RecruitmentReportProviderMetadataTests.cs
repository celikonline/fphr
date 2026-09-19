using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Infrastructure.Reporting;
using Sense.Modules.Reporting.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// Container-free checks for the ATS report providers' catalog contract (R5, roadmap #94): each report is gated by a
/// real Recruitment permission (the Reporting module enforces <see cref="IReportProvider.RequiredPermission"/> both
/// when listing the catalog and when generating), keys are unique, and the date-range parameter is declared the way
/// the report needs it. Generation itself is covered by <see cref="RecruitmentReportProviderTests"/> (PostgreSQL).
/// </summary>
public sealed class RecruitmentReportProviderMetadataTests
{
    // The metadata members never touch the constructor dependencies, so nulls are enough here.
    private static IReadOnlyList<IReportProvider> Providers() =>
    [
        new RecruitmentOverviewReportProvider(null!, null!, null!),
        new RecruitmentDailyCountsReportProvider(null!, null!, null!),
        new RecruitmentCandidatesExportReportProvider(null!, null!, null!),
    ];

    [Fact]
    public void Keys_AreUnique_AndUseRecruitmentPrefix()
    {
        var keys = Providers().Select(p => p.Key).ToList();

        keys.ShouldBe(["recruitment.overview", "recruitment.daily_counts", "recruitment.candidates_export"]);
        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    [Fact]
    public void EveryProvider_RequiresARecruitmentPermission()
    {
        foreach (var provider in Providers())
        {
            provider.RequiredPermission.ShouldNotBeNull();
            provider.RequiredPermission.ShouldStartWith("recruitment.");
        }
    }

    [Fact]
    public void CandidateExport_RequiresCandidatesRead_WhileFunnelReportsRequireApplicationsRead()
    {
        var byKey = Providers().ToDictionary(p => p.Key);

        byKey[RecruitmentCandidatesExportReportProvider.ReportKey].RequiredPermission.ShouldBe(RecruitmentPermissions.CandidatesRead.Key);
        byKey[RecruitmentOverviewReportProvider.ReportKey].RequiredPermission.ShouldBe(RecruitmentPermissions.ApplicationsRead.Key);
        byKey[RecruitmentDailyCountsReportProvider.ReportKey].RequiredPermission.ShouldBe(RecruitmentPermissions.ApplicationsRead.Key);
    }

    [Fact]
    public void Categories_AreKnownCatalogCategories()
    {
        foreach (var provider in Providers())
        {
            ReportCategories.All.ShouldContain(provider.CategoryKey);
        }
    }

    [Fact]
    public void DateRange_IsRequiredOnlyForDailyCounts()
    {
        var byKey = Providers().ToDictionary(p => p.Key);

        byKey[RecruitmentDailyCountsReportProvider.ReportKey].Parameters.Single().IsRequired.ShouldBeTrue();
        byKey[RecruitmentOverviewReportProvider.ReportKey].Parameters.Single().IsRequired.ShouldBeFalse();
        byKey[RecruitmentCandidatesExportReportProvider.ReportKey].Parameters.Single().IsRequired.ShouldBeFalse();
        Providers().ShouldAllBe(p => p.Parameters.Single().Kind == ReportParameterKind.DateRange);
    }
}
