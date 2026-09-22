using Sense.Modules.OccupationalSafety.Application.Services;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.Services;
using Xunit;

namespace Sense.Modules.OccupationalSafety.Tests;

public class OhsServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static SafetyAssignment Expert(SafetyExpertCertificateClass cls, int minutes) =>
        SafetyAssignment.Create(TenantId, LocationId, SafetyServiceRole.SafetyExpert, "Uzman", null, cls, null, null, minutes, Start).Value;

    private static SafetyAssignment Physician(int minutes) =>
        SafetyAssignment.Create(TenantId, LocationId, SafetyServiceRole.WorkplacePhysician, "Hekim", null, null, null, null, minutes, Start).Value;

    private static OhsCommitteeMember Member(CommitteeRole role) =>
        OhsCommitteeMember.Create(TenantId, LocationId, role, role.ToString(), null, Start).Value;

    [Theory]
    [InlineData(SafetyServiceRole.SafetyExpert, HazardClass.High, 40)]
    [InlineData(SafetyServiceRole.SafetyExpert, HazardClass.Low, 10)]
    [InlineData(SafetyServiceRole.WorkplacePhysician, HazardClass.Medium, 10)]
    [InlineData(SafetyServiceRole.OtherHealthPersonnel, HazardClass.Medium, 0)]
    public void Norms_MinutesPerEmployee(SafetyServiceRole role, HazardClass hazard, int expected) =>
        Assert.Equal(expected, OhsServiceNorms.MinutesPerEmployeePerMonth(role, hazard));

    [Theory]
    [InlineData(40, 1)]
    [InlineData(51, 2)]
    [InlineData(600, 4)]
    public void Norms_EmployeeRepresentatives(int employees, int expected) =>
        Assert.Equal(expected, OhsServiceNorms.EmployeeRepresentativeCount(employees));

    [Fact]
    public void SafetyExpert_WithoutCertificate_IsRejected()
    {
        var result = SafetyAssignment.Create(TenantId, LocationId, SafetyServiceRole.SafetyExpert, "Uzman", null, null, null, null, 100, Start);

        Assert.Equal(OhsDomainErrors.AssignmentCertificateRequired, result.Error.Code);
    }

    [Fact]
    public void Summary_VeryHazardous60Employees_FlagsEveryGap()
    {
        var summary = ListLocationServiceSummariesHandler.Summarize(
            LocationId, HazardClass.High, 60, [Expert(SafetyExpertCertificateClass.B, 1000), Physician(900)], [Member(CommitteeRole.Chair)]);

        // 60 x 40 = 2400 expert minutes, 60 x 15 = 900 physician minutes, 60 x 10 = 600 DSP minutes.
        Assert.Equal(2400, summary.Minutes.Single(m => m.Role == SafetyServiceRole.SafetyExpert).RequiredMinutes);
        Assert.True(summary.Minutes.Single(m => m.Role == SafetyServiceRole.WorkplacePhysician).IsMet);
        Assert.Equal(1, summary.CommitteeMeetingIntervalMonths);
        Assert.Equal(2, summary.RequiredRepresentativeCount);
        Assert.Equal(
            ["insufficient_minutes_safety_expert", "missing_other_health_personnel", "certificate_class", "committee_missing", "representatives"],
            summary.Issues);
    }

    [Fact]
    public void Summary_SmallLessHazardousWorkplace_NeedsNoCommittee()
    {
        var summary = ListLocationServiceSummariesHandler.Summarize(
            LocationId, HazardClass.Low, 12, [Expert(SafetyExpertCertificateClass.C, 120), Physician(60)], []);

        Assert.False(summary.CommitteeRequired);
        Assert.Empty(summary.Issues);
    }

    [Fact]
    public void EndingAnAssignment_BeforeItStarted_IsRejected()
    {
        var assignment = Physician(60);

        Assert.Equal(OhsDomainErrors.AssignmentEndBeforeStart, assignment.End(Start.AddDays(-1)).Error.Code);
        Assert.True(assignment.End(Start.AddMonths(3)).IsSuccess);
        Assert.False(assignment.IsActiveOn(Start.AddMonths(4)));
    }
}
