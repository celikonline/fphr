using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Candidates;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class CandidateTests
{
    [Fact]
    public void Create_Default_SourceIsManual()
    {
        var result = Candidate.Create(Guid.NewGuid(), "Ada", "Yılmaz", "ada.yilmaz@example.com", null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Source.ShouldBe(CandidateSource.Manual);
    }

    [Fact]
    public void Create_FromCareerSite_SourceIsCareerSite()
    {
        var result = Candidate.Create(
            Guid.NewGuid(), "Elif", "Kara", "elif.kara@example.com", null, source: CandidateSource.CareerSite);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Source.ShouldBe(CandidateSource.CareerSite);
    }

    [Fact]
    public void Create_InvalidEmail_IsRejected()
    {
        var result = Candidate.Create(Guid.NewGuid(), "Ada", "Yılmaz", "not-an-email", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.CandidateEmailRequired);
    }
}
