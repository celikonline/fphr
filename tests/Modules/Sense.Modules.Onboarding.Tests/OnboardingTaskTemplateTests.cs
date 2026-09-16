using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Sense.Modules.Onboarding.Tests;

public sealed class OnboardingTaskTemplateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_ValidInput_Succeeds()
    {
        var result = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "Zimmet teslimi", "it", 1, true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Stage.ShouldBe(OnboardingStage.Onboarding);
        result.Value.Title.ShouldBe("Zimmet teslimi");
        result.Value.ResponsibleRoleCode.ShouldBe("it");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Create_NegativeDueDateOffset_Succeeds_MeaningBeforeReferenceDate()
    {
        // SGK işe giriş bildirgesi must reach SGK before the employee's actual start date (G-4-14).
        var result = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "SGK işe giriş bildirgesi", "hr", -1, true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DueDateOffsetDays.ShouldBe(-1);
    }

    [Fact]
    public void Create_EmptyTitle_Fails()
    {
        var result = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "  ", "hr", 0, true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.TaskTemplateTitleRequired);
    }

    [Fact]
    public void Create_TitleTooLong_Fails()
    {
        var title = new string('a', OnboardingLimits.TaskTemplateTitleMaxLength + 1);

        var result = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Offboarding, title, "hr", 10, false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.TaskTemplateTitleTooLong);
    }

    [Fact]
    public void Create_EmptyResponsibleRoleCode_Fails()
    {
        var result = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "Oryantasyon", " ", 3, true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.TaskTemplateResponsibleRoleRequired);
    }
}
