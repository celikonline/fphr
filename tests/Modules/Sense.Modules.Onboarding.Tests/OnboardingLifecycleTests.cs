using NSubstitute;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Onboarding.Application;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Contracts.Events;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Events;
using Xunit;

namespace Sense.Modules.Onboarding.Tests;

public class OnboardingLifecycleTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IOnboardingTaskTemplateRepository _templates = Substitute.For<IOnboardingTaskTemplateRepository>();
    private readonly IEmployeeOnboardingTaskRepository _tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
    private readonly IOnboardingUnitOfWork _unitOfWork = Substitute.For<IOnboardingUnitOfWork>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly List<EmployeeOnboardingTask> _added = [];

    public OnboardingLifecycleTests() => _tasks.Add(Arg.Do<EmployeeOnboardingTask>(_added.Add));

    private OnboardingTaskTemplate Template(OnboardingStage stage, string title, int offsetDays) =>
        OnboardingTaskTemplate.Create(_tenantId, stage, title, "hr", offsetDays, isMandatory: true).Value;

    [Fact]
    public async Task EnsureSeeded_AddsTheRecommendedTemplatesOnlyWhenTheTenantHasNone()
    {
        var added = new List<OnboardingTaskTemplate>();
        _templates.Add(Arg.Do<OnboardingTaskTemplate>(added.Add));
        _templates.AnyAsync(Arg.Any<CancellationToken>()).Returns(false);

        var count = await DefaultOnboardingTemplates.EnsureSeededAsync(_tenantId, _templates, CancellationToken.None);

        Assert.Equal(added.Count, count);
        Assert.Contains(added, t => t.Stage == OnboardingStage.Onboarding && t.Title.Contains("SGK işe giriş") && t.DueDateOffsetDays == -1);
        Assert.Contains(added, t => t.Stage == OnboardingStage.Offboarding && t.Title.Contains("Çalışma belgesi"));

        _templates.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        Assert.Equal(0, await DefaultOnboardingTemplates.EnsureSeededAsync(_tenantId, _templates, CancellationToken.None));
    }

    [Fact]
    public async Task EmployeeHired_MaterializesOnboardingTasksRelativeToTheHireDate_AndSaves()
    {
        var employeeId = Guid.NewGuid();
        _templates.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        var sgk = Template(OnboardingStage.Onboarding, "SGK işe giriş", -1);
        var probation = Template(OnboardingStage.Onboarding, "Deneme süresi", 55);
        _templates.ListActiveByStageAsync(OnboardingStage.Onboarding, Arg.Any<CancellationToken>()).Returns([sgk, probation]);
        var hireDate = new DateOnly(2026, 10, 1);

        await new EmployeeHiredOnboardingHandler(_templates, _tasks, _unitOfWork, _outbox)
            .Handle(new EmployeeHired(_tenantId, employeeId, hireDate, hireDate), CancellationToken.None);

        Assert.Equal(2, _added.Count);
        Assert.Equal(new DateOnly(2026, 9, 30), _added.Single(t => t.TemplateId == sgk.Id).DueDate);
        Assert.Equal(new DateOnly(2026, 11, 25), _added.Single(t => t.TemplateId == probation.Id).DueDate);
        Assert.All(_added, t => Assert.Equal(employeeId, t.EmployeeId));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _outbox.Received(2).Enqueue(Arg.Any<OnboardingTaskAssigned>());
    }

    [Fact]
    public async Task EmployeeHired_Redelivered_DoesNotDuplicateTasks()
    {
        var employeeId = Guid.NewGuid();
        _templates.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        var sgk = Template(OnboardingStage.Onboarding, "SGK işe giriş", -1);
        _templates.ListActiveByStageAsync(OnboardingStage.Onboarding, Arg.Any<CancellationToken>()).Returns([sgk]);
        _tasks.FindAsync(employeeId, sgk.Id, Arg.Any<CancellationToken>())
            .Returns(EmployeeOnboardingTask.Materialize(_tenantId, employeeId, sgk.Id, OnboardingStage.Onboarding, new DateOnly(2026, 9, 30)));

        await new EmployeeHiredOnboardingHandler(_templates, _tasks, _unitOfWork, _outbox)
            .Handle(new EmployeeHired(_tenantId, employeeId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.Empty(_added);
        _outbox.DidNotReceive().Enqueue(Arg.Any<OnboardingTaskAssigned>());
    }

    [Fact]
    public async Task EmployeeTerminated_WithoutTerminationDate_UsesTodayForOffboardingDueDates()
    {
        var employeeId = Guid.NewGuid();
        _templates.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        var sgkExit = Template(OnboardingStage.Offboarding, "SGK işten ayrılış", 10);
        _templates.ListActiveByStageAsync(OnboardingStage.Offboarding, Arg.Any<CancellationToken>()).Returns([sgkExit]);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero));

        await new EmployeeTerminatedOffboardingHandler(_templates, _tasks, _unitOfWork, _outbox, clock)
            .Handle(new EmployeeTerminated(_tenantId, employeeId, default, "04", null), CancellationToken.None);

        var task = Assert.Single(_added);
        Assert.Equal(OnboardingStage.Offboarding, task.Stage);
        Assert.Equal(new DateOnly(2026, 10, 2), task.DueDate);
    }

    [Fact]
    public async Task ListPending_JoinsTemplateTitlesAndFlagsOverdueTasks()
    {
        var employeeId = Guid.NewGuid();
        var contract = Template(OnboardingStage.Onboarding, "İş sözleşmesi", 0);
        _templates.ListAsync(Arg.Any<CancellationToken>()).Returns([contract]);
        _tasks.ListPendingAsync(Arg.Any<CancellationToken>()).Returns(
        [
            EmployeeOnboardingTask.Materialize(_tenantId, employeeId, contract.Id, OnboardingStage.Onboarding, new DateOnly(2026, 9, 20)),
        ]);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero));

        var result = await new ListPendingOnboardingTasksHandler(_tasks, _templates, clock)
            .Handle(new ListPendingOnboardingTasksQuery(), CancellationToken.None);

        var item = Assert.Single(result.Value);
        Assert.Equal("İş sözleşmesi", item.Title);
        Assert.True(item.IsOverdue);
        Assert.True(item.IsMandatory);
    }
}
