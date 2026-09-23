using NSubstitute;
using Sense.Modules.Recruitment.Application.JobPostings;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// TK-023 (roadmap #83): "Admin - teklif şablonu" ikinci rolü için dar kapsamlı izin anahtarı, ve bir ilanın
/// erişim listesini gösteren ayrı bir sorgu/uç nokta.
/// </summary>
public sealed class JobPostingAccessTests
{
    private static JobPosting NewDraftWithAccess(params Guid[] userIds)
    {
        var posting = JobPosting.Create(
            Guid.NewGuid(), "Senior Backend Developer", Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, null, Guid.NewGuid(), null).Value;
        posting.SetAccess(userIds);
        return posting;
    }

    [Fact]
    public void OfferTemplatesManage_IsASeparateKeyFromTheGeneralPostingsPermissions()
    {
        RecruitmentPermissions.OfferTemplatesManage.Key.ShouldBe("recruitment.offer_templates.manage");
        RecruitmentPermissions.OfferTemplatesManage.Key.ShouldNotBe(RecruitmentPermissions.PostingsUpdate.Key);
        RecruitmentPermissions.All.ShouldContain(RecruitmentPermissions.OfferTemplatesManage);
    }

    [Fact]
    public void GetJobPostingAccessQuery_RequiresPostingsReadPermission()
    {
        var permissions = typeof(GetJobPostingAccessQuery)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .Cast<RequiresPermissionAttribute>()
            .Select(a => a.Permission)
            .ToList();

        permissions.ShouldContain(RecruitmentPermissions.PostingsRead.Key);
        permissions.ShouldNotContain(RecruitmentPermissions.OfferTemplatesManage.Key);
    }

    [Fact]
    public void SetJobPostingAccessCommand_RequiresPostingsUpdatePermission()
    {
        var permissions = typeof(SetJobPostingAccessCommand)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .Cast<RequiresPermissionAttribute>()
            .Select(a => a.Permission)
            .ToList();

        permissions.ShouldContain(RecruitmentPermissions.PostingsUpdate.Key);
    }

    [Fact]
    public async Task GetJobPostingAccessHandler_ReturnsTheExplicitlyGrantedUserIds()
    {
        var userId = Guid.NewGuid();
        var posting = NewDraftWithAccess(userId);
        var postings = Substitute.For<IJobPostingRepository>();
        postings.GetByIdAsync(posting.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(posting));
        var handler = new GetJobPostingAccessHandler(postings);

        var result = await handler.Handle(new GetJobPostingAccessQuery(posting.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([userId]);
    }

    [Fact]
    public async Task GetJobPostingAccessHandler_UnknownPosting_ReturnsTheRepositoryFailure()
    {
        var postingId = Guid.NewGuid();
        var postings = Substitute.For<IJobPostingRepository>();
        var notFound = Error.NotFound(RecruitmentDomainErrors.PostingTitleRequired);
        postings.GetByIdAsync(postingId, Arg.Any<CancellationToken>()).Returns(Result.Failure<JobPosting>(notFound));
        var handler = new GetJobPostingAccessHandler(postings);

        var result = await handler.Handle(new GetJobPostingAccessQuery(postingId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(notFound);
    }
}
