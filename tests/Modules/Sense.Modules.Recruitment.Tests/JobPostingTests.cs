using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class JobPostingTests
{
    private static JobPosting NewDraft() =>
        JobPosting.Create(Guid.NewGuid(), "Senior Backend Developer", Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, null, Guid.NewGuid(), null).Value;

    [Fact]
    public void Create_BlankTitle_IsRejected()
    {
        var result = JobPosting.Create(Guid.NewGuid(), "   ", Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, null, Guid.NewGuid(), null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingTitleRequired);
    }

    [Fact]
    public void Create_AssignsAPermanentApplySlug()
    {
        var posting = NewDraft();

        posting.ApplySlug.ShouldNotBeNullOrWhiteSpace();
        posting.Status.ShouldBe(JobPostingStatus.Draft);
    }

    [Fact]
    public void Publish_FromDraft_OpensAndStampsPublishedAtOnce()
    {
        var posting = NewDraft();

        posting.Publish().IsSuccess.ShouldBeTrue();
        posting.Status.ShouldBe(JobPostingStatus.Open);
        var firstPublishedAt = posting.PublishedAt;
        firstPublishedAt.ShouldNotBeNull();

        posting.Close().IsSuccess.ShouldBeTrue();
        posting.Publish().IsSuccess.ShouldBeTrue();

        posting.PublishedAt.ShouldBe(firstPublishedAt);
    }

    [Fact]
    public void Close_WhenNotOpen_IsRejected()
    {
        var posting = NewDraft();

        var result = posting.Close();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingNotCloseable);
    }

    [Fact]
    public void Archive_IsTerminal()
    {
        var posting = NewDraft();
        posting.Publish();

        posting.Archive(JobPostingArchiveReason.PositionFilled).IsSuccess.ShouldBeTrue();

        var reopen = posting.Publish();
        reopen.IsFailure.ShouldBeTrue();
        reopen.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingNotOpenable);

        var archiveAgain = posting.Archive(JobPostingArchiveReason.Other);
        archiveAgain.IsFailure.ShouldBeTrue();
        archiveAgain.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingAlreadyArchived);
    }

    [Fact]
    public void SetAccess_DeduplicatesUserIds()
    {
        var posting = NewDraft();
        var userId = Guid.NewGuid();

        posting.SetAccess([userId, userId, Guid.NewGuid()]);

        posting.Access.Count.ShouldBe(2);
    }
}
