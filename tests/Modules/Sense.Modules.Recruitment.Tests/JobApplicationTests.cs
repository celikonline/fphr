using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class JobApplicationTests
{
    [Fact]
    public void Create_StartsAtAppliedStage()
    {
        var application = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        application.Stage.ShouldBe(ApplicationStage.Applied);
        application.ArchivedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(ApplicationStage.Hired)]
    [InlineData(ApplicationStage.Rejected)]
    public void MoveStage_ToTerminalStage_StampsArchivedAt(ApplicationStage stage)
    {
        var application = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = application.MoveStage(stage);

        result.IsSuccess.ShouldBeTrue();
        application.ArchivedAt.ShouldNotBeNull();
    }

    [Fact]
    public void MoveStage_AfterTerminalStage_IsRejected()
    {
        var application = JobApplication.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        application.MoveStage(ApplicationStage.Rejected);

        var result = application.MoveStage(ApplicationStage.Screening);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.ApplicationAlreadyTerminal);
    }
}
