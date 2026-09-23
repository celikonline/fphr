using NSubstitute;
using Sense.Modules.Onboarding.Application;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Onboarding.Tests;

/// <summary>TK-006: self-service "me" checklist - an employee can only see/complete their own materialized tasks.</summary>
public sealed class MyOnboardingTasksTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();

    private static ICurrentUser CurrentUser(Guid? employeeId, Guid? userId = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        currentUser.UserId.Returns(userId ?? Guid.NewGuid());
        return currentUser;
    }

    private static OnboardingTaskTemplate ActiveTemplate() =>
        OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "SGK işe giriş", "hr", -1, true).Value;

    [Fact]
    public async Task ListMyTasks_ReturnsOnlyTheCallersOwnTasks_NotTheWholeTenant()
    {
        var template = ActiveTemplate();
        var templates = Substitute.For<IOnboardingTaskTemplateRepository>();
        templates.ListAsync(Arg.Any<CancellationToken>()).Returns([template]);
        var myTask = EmployeeOnboardingTask.Materialize(TenantId, CallerEmployeeId, template.Id, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1));
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.ListByEmployeeAsync(CallerEmployeeId, Arg.Any<CancellationToken>()).Returns([myTask]);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero));

        var handler = new ListMyOnboardingTasksHandler(CurrentUser(CallerEmployeeId), tasks, templates, clock);
        var result = await handler.Handle(new ListMyOnboardingTasksQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().EmployeeId.ShouldBe(CallerEmployeeId);
        // Never asked the repository for the tenant-wide pending list or another employee's tasks.
        await tasks.DidNotReceive().ListPendingAsync(Arg.Any<CancellationToken>());
        await tasks.DidNotReceive().ListByEmployeeAsync(OtherEmployeeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMyTasks_CallerHasNoLinkedEmployee_ReturnsNotFound_AndDoesNotQueryTasks()
    {
        var templates = Substitute.For<IOnboardingTaskTemplateRepository>();
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        var handler = new ListMyOnboardingTasksHandler(CurrentUser(employeeId: null), tasks, templates, clock);
        var result = await handler.Handle(new ListMyOnboardingTasksQuery(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingErrors.NoEmployeeForUser);
        await tasks.DidNotReceive().ListByEmployeeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteMyTask_OwnPendingTask_MarksCompleted()
    {
        var template = ActiveTemplate();
        var myTask = EmployeeOnboardingTask.Materialize(TenantId, CallerEmployeeId, template.Id, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1));
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.GetByIdAsync(myTask.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(myTask)));
        var currentUser = CurrentUser(CallerEmployeeId, userId: CallerEmployeeId);

        var handler = new CompleteMyTaskHandler(currentUser, tasks);
        var result = await handler.Handle(new CompleteMyTaskCommand(myTask.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(EmployeeOnboardingTaskStatus.Completed);
        result.Value.CompletedBy.ShouldBe(CallerEmployeeId);
    }

    /// <summary>Yetki testi: bir çalışan başka bir çalışanın görevini kendi 'me' uç noktasından tamamlayamaz.</summary>
    [Fact]
    public async Task CompleteMyTask_AnotherEmployeesTask_IsForbidden_AndDoesNotCompleteIt()
    {
        var template = ActiveTemplate();
        var othersTask = EmployeeOnboardingTask.Materialize(TenantId, OtherEmployeeId, template.Id, OnboardingStage.Onboarding, new DateOnly(2026, 10, 1));
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        tasks.GetByIdAsync(othersTask.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(othersTask)));
        var currentUser = CurrentUser(CallerEmployeeId);

        var handler = new CompleteMyTaskHandler(currentUser, tasks);
        var result = await handler.Handle(new CompleteMyTaskCommand(othersTask.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingErrors.NotOwner);
        othersTask.Status.ShouldBe(EmployeeOnboardingTaskStatus.Pending);
    }

    [Fact]
    public async Task CompleteMyTask_CallerHasNoLinkedEmployee_ReturnsNotFound_AndDoesNotLoadTask()
    {
        var tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
        var handler = new CompleteMyTaskHandler(CurrentUser(employeeId: null), tasks);

        var result = await handler.Handle(new CompleteMyTaskCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingErrors.NoEmployeeForUser);
        await tasks.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
