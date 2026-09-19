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
    public void Create_And_UpdateContactInfo_StoreEmailLowerCased()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), "Ada", "Yılmaz", "Ada.Yilmaz@EXAMPLE.com", null).Value;
        candidate.Email.ShouldBe("ada.yilmaz@example.com");

        candidate.UpdateContactInfo("Ada", "Yılmaz", "X@Example.COM", null).IsSuccess.ShouldBeTrue();
        candidate.Email.ShouldBe("x@example.com");
    }

    [Fact]
    public void NormalizeEmail_UsesInvariantCasing_NotCurrentCulture()
    {
        // "I" must become "i" (not the Turkish dotless "ı") regardless of the machine culture.
        Candidate.NormalizeEmail("INFO@EXAMPLE.COM").ShouldBe("info@example.com");
    }

    [Fact]
    public void Create_InvalidEmail_IsRejected()
    {
        var result = Candidate.Create(Guid.NewGuid(), "Ada", "Yılmaz", "not-an-email", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.CandidateEmailRequired);
    }

    [Fact]
    public void Anonymize_ScrubsPersonalDataAndTags()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), "Ada", "Yılmaz", "ada.yilmaz@example.com", "5551234567").Value;
        candidate.SetTags([Guid.NewGuid()]);

        var result = candidate.Anonymize();

        result.IsSuccess.ShouldBeTrue();
        candidate.IsAnonymized.ShouldBeTrue();
        candidate.FirstName.ShouldNotBe("Ada");
        candidate.LastName.ShouldNotBe("Yılmaz");
        candidate.Email.ShouldEndWith(Candidate.AnonymizedEmailDomain);
        candidate.Phone.ShouldBeNull();
        candidate.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void Anonymize_CalledTwice_SecondCallFails()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), "Ada", "Yılmaz", "ada.yilmaz@example.com", null).Value;
        candidate.Anonymize();

        var result = candidate.Anonymize();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.CandidateAlreadyAnonymized);
    }
}
