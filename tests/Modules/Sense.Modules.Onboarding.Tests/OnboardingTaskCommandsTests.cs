using NSubstitute;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Onboarding.Tests;

public sealed class OnboardingTaskCommandsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static OnboardingTaskTemplate ActiveTemplate(int offsetDays = 1) =>
        OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "Zimmet teslimi", "it", offsetDays, true).Value;

    [Fact]
    public async Task CreateTaskTemplate_ValidInput_AddsToRepository()
    {
        var templates = Substitute.For<IOnboardingTaskTemplateRepository>();
        var handler = new CreateOnboardingTaskTemplateHandler(Tenant(), templates);

        var result = await handler.Handle(
            new CreateOnboardingTaskTemplateCommand(OnboardingStage.Onboarding, "Oryantasyon eğitimi", "hr", 3, true),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe("Oryantasyon eğitimi");
        templates.Received(1).Add(Arg.Is<OnboardingTaskTemplate>(t => t.Title == "Oryantasyon eğitimi"));
    }

    [Fact]
    public async Task CreateTaskTemplate_EmptyTitle_Fails_AndDoesNotAdd()
    {
        var templates = Substitute.For<IOnboardingTaskTemplateRepository>();
        var handler = new CreateOnboardingTaskTemplateHandler(Tenant(), templates);

        var result = await handler.Handle(
            new CreateOnboardingTaskTemplateCommand(OnboardingStage.Onboarding, " ", "hr", 3, true),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.TaskTemplateTitleRequired);
        templates.DidNotReceive().Add(Arg.Any<OnboardingTaskTemplate>());
    }

    [Fact]
    public async Task MaterializeTasksForEmployee_ActiveTemplates_CreatesOneTaskPerTemplate()
    {
        var template = ActiveTemplate(offsetDays: -1);
        var templates = Substitute.For<IOnboardingTaskTemplateRepository>();
        templates.ListActiveByStageAsync(OnboardingStage.Onboarding, Arg.Any<CancellationToken>())
            .Returns([template]);
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.FindAsync(EmployeeId, template.Id, Arg.Any<CancellationToken>()).Returns((EmployeeOnboardingTask?)null);

        var handler = new MaterializeTasksForEmployeeHandler(Tenant(), templates, tasks);
        var referenceDate = new DateOnly(2026, 10, 1);

        var result = await handler.Handle(
            new MaterializeTasksForEmployeeCommand(EmployeeId, OnboardingStage.Onboarding, referenceDate), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().DueDate.ShouldBe(referenceDate.AddDays(-1));
        tasks.Received(1).Add(Arg.Is<EmployeeOnboardingTask>(t => t.EmployeeId == EmployeeId && t.TemplateId == template.Id));
    }

    [Fact]
    public async Task MaterializeTasksForEmployee_AlreadyMaterialized_SkipsTemplate_Idempotent()
    {
        var template = ActiveTemplate();
        var existing = EmployeeOnboardingTask.Materialize(TenantId, EmployeeId, template.Id, OnboardingStage.Onboarding, new DateOnly(2026, 10, 2));
        var templates = Substitute.For<IOnboardingTaskTemplateRepository>();
        templates.ListActiveByStageAsync(OnboardingStage.Onboarding, Arg.Any<CancellationToken>())
            .Returns([template]);
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.FindAsync(EmployeeId, template.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new MaterializeTasksForEmployeeHandler(Tenant(), templates, tasks);

        var result = await handler.Handle(
            new MaterializeTasksForEmployeeCommand(EmployeeId, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        tasks.DidNotReceive().Add(Arg.Any<EmployeeOnboardingTask>());
    }

    [Fact]
    public async Task CompleteTask_PendingTask_MarksCompleted()
    {
        var template = ActiveTemplate();
        var task = EmployeeOnboardingTask.Materialize(TenantId, EmployeeId, template.Id, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1));
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(task)));
        var currentUser = Substitute.For<ICurrentUser>();
        var completedBy = Guid.NewGuid();
        currentUser.UserId.Returns(completedBy);

        var handler = new CompleteTaskHandler(currentUser, tasks);
        var result = await handler.Handle(new CompleteTaskCommand(task.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(EmployeeOnboardingTaskStatus.Completed);
        result.Value.CompletedBy.ShouldBe(completedBy);
    }

    [Fact]
    public async Task WaiveTask_PendingTask_MarksWaived()
    {
        var template = ActiveTemplate();
        var task = EmployeeOnboardingTask.Materialize(TenantId, EmployeeId, template.Id, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1));
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(task)));
        var currentUser = Substitute.For<ICurrentUser>();

        var handler = new WaiveTaskHandler(currentUser, tasks);
        var result = await handler.Handle(new WaiveTaskCommand(task.Id, "kapsam dışı"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(EmployeeOnboardingTaskStatus.Waived);
        result.Value.WaivedReason.ShouldBe("kapsam dışı");
    }

    [Fact]
    public async Task WaiveTask_AlreadyCompletedTask_Fails()
    {
        var template = ActiveTemplate();
        var task = EmployeeOnboardingTask.Materialize(TenantId, EmployeeId, template.Id, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1));
        task.Complete(Guid.NewGuid());
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(task)));
        var currentUser = Substitute.For<ICurrentUser>();

        var handler = new WaiveTaskHandler(currentUser, tasks);
        var result = await handler.Handle(new WaiveTaskCommand(task.Id, "gerekçe"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.EmployeeTaskAlreadyFinalized);
    }
}
