using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.Tags;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class TagTests
{
    [Fact]
    public void Create_BlankName_IsRejected()
    {
        var result = Tag.Create(Guid.NewGuid(), "   ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.TagNameRequired);
    }

    [Fact]
    public void Deactivate_Twice_IsRejected()
    {
        var tag = Tag.Create(Guid.NewGuid(), "Senior").Value;
        tag.Deactivate();

        var result = tag.Deactivate();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.TagAlreadyInactive);
    }

    [Fact]
    public void Reactivate_MakesItActiveAgain()
    {
        var tag = Tag.Create(Guid.NewGuid(), "Senior").Value;
        tag.Deactivate();

        tag.Reactivate();

        tag.IsActive.ShouldBeTrue();
    }
}

public sealed class CandidateTagAssignmentTests
{
    [Fact]
    public void SetTags_DeduplicatesTagIds()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", null).Value;
        var tagId = Guid.NewGuid();

        candidate.SetTags([tagId, tagId, Guid.NewGuid()]);

        candidate.Tags.Count.ShouldBe(2);
    }

    [Fact]
    public void SetTags_ReplacesThePreviousSet()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", null).Value;
        candidate.SetTags([Guid.NewGuid()]);

        var newTagId = Guid.NewGuid();
        candidate.SetTags([newTagId]);

        candidate.Tags.Count.ShouldBe(1);
        candidate.Tags[0].TagId.ShouldBe(newTagId);
    }
}
