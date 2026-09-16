using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.AutomationRules;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class AutomationRuleTests
{
    [Fact]
    public void Create_SameFromAndToStage_IsRejected()
    {
        var result = AutomationRule.Create(Guid.NewGuid(), ApplicationStage.Interview, ApplicationStage.Interview, Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.AutomationRuleSameStage);
    }

    [Fact]
    public void Create_DifferentStages_Succeeds()
    {
        var result = AutomationRule.Create(Guid.NewGuid(), ApplicationStage.Interview, ApplicationStage.Offer, Guid.NewGuid());

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Deactivate_Twice_IsRejected()
    {
        var rule = AutomationRule.Create(Guid.NewGuid(), ApplicationStage.Interview, ApplicationStage.Offer, Guid.NewGuid()).Value;
        rule.Deactivate();

        var result = rule.Deactivate();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.AutomationRuleAlreadyInactive);
    }
}
