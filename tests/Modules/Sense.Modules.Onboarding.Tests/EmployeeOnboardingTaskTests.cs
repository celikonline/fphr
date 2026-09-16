using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Sense.Modules.Onboarding.Tests;

public sealed class EmployeeOnboardingTaskTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TemplateId = Guid.NewGuid();

    private static EmployeeOnboardingTask NewTask() =>
        EmployeeOnboardingTask.Materialize(TenantId, EmployeeId, TemplateId, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1));

    [Fact]
    public void Complete_PendingTask_SetsCompletedStatus()
    {
        var task = NewTask();
        var completedBy = Guid.NewGuid();

        var result = task.Complete(completedBy);

        result.IsSuccess.ShouldBeTrue();
        task.Status.ShouldBe(EmployeeOnboardingTaskStatus.Completed);
        task.CompletedBy.ShouldBe(completedBy);
        task.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Complete_AlreadyCompletedTask_Fails_AndLeavesStateUnchanged()
    {
        var task = NewTask();
        var firstCompletedBy = Guid.NewGuid();
        task.Complete(firstCompletedBy);

        var result = task.Complete(Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.EmployeeTaskAlreadyFinalized);
        task.CompletedBy.ShouldBe(firstCompletedBy);
    }

    [Fact]
    public void Waive_PendingTask_SetsWaivedStatus()
    {
        var task = NewTask();
        var waivedBy = Guid.NewGuid();

        var result = task.Waive("çalışan zaten sertifikaya sahip", waivedBy);

        result.IsSuccess.ShouldBeTrue();
        task.Status.ShouldBe(EmployeeOnboardingTaskStatus.Waived);
        task.WaivedBy.ShouldBe(waivedBy);
        task.WaivedReason.ShouldBe("çalışan zaten sertifikaya sahip");
    }

    [Fact]
    public void Waive_EmptyReason_Fails_AndLeavesStatusPending()
    {
        var task = NewTask();

        var result = task.Waive(" ", Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.EmployeeTaskWaiveReasonRequired);
        task.Status.ShouldBe(EmployeeOnboardingTaskStatus.Pending);
    }

    [Fact]
    public void Waive_AlreadyCompletedTask_Fails()
    {
        var task = NewTask();
        task.Complete(Guid.NewGuid());

        var result = task.Waive("gerekçe", Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.EmployeeTaskAlreadyFinalized);
        task.Status.ShouldBe(EmployeeOnboardingTaskStatus.Completed);
    }

    [Fact]
    public void Complete_AlreadyWaivedTask_Fails()
    {
        var task = NewTask();
        task.Waive("gerekçe", Guid.NewGuid());

        var result = task.Complete(Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.EmployeeTaskAlreadyFinalized);
        task.Status.ShouldBe(EmployeeOnboardingTaskStatus.Waived);
    }
}
