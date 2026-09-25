using NSubstitute;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Shouldly;
using Xunit;

namespace Sense.Modules.Onboarding.Tests;

public sealed class OnboardingCategoryAndSetTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Reference = new(2026, 10, 1);

    private readonly IOnboardingTemplateSetRepository _sets = Substitute.For<IOnboardingTemplateSetRepository>();
    private readonly IOnboardingTaskTemplateRepository _templates = Substitute.For<IOnboardingTaskTemplateRepository>();
    private readonly IEmployeeOnboardingTaskRepository _tasks = Substitute.For<IEmployeeOnboardingTaskRepository>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly List<EmployeeOnboardingTask> _added = [];

    public OnboardingCategoryAndSetTests() => _tasks.Add(Arg.Do<EmployeeOnboardingTask>(_added.Add));

    private StartProcessHandler Handler()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return new StartProcessHandler(tenant, _sets, _templates, _tasks, _outbox);
    }

    private static OnboardingTaskTemplate Template(string title, Guid? categoryId, OnboardingImportance importance = OnboardingImportance.Normal) =>
        OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, title, "hr", 0, true, categoryId, importance, requiresEDocument: true).Value;

    [Fact]
    public async Task Start_from_named_set_creates_tasks_grouped_by_category()
    {
        var docs = Guid.NewGuid();
        var it = Guid.NewGuid();
        var a = Template("Sözleşme", docs, OnboardingImportance.Critical);
        var b = Template("Özlük", docs);
        var c = Template("Hesap", it);
        var outside = Template("Set dışı", it);
        var set = OnboardingTemplateSet.Create(TenantId, OnboardingStage.Onboarding, "Ofis", [a.Id, b.Id, c.Id]).Value;
        _sets.GetByIdAsync(set.Id, Arg.Any<CancellationToken>()).Returns(set);
        _templates.ListAsync(Arg.Any<CancellationToken>()).Returns([a, b, c, outside]);

        var result = await Handler().Handle(new StartProcessCommand(EmployeeId, set.Id, Reference), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(3);
        result.Value.GroupBy(t => t.CategoryId).ToDictionary(g => g.Key!.Value, g => g.Count())
            .ShouldBe(new Dictionary<Guid, int> { [docs] = 2, [it] = 1 });
        _added.ShouldAllBe(t => t.TemplateSetId == set.Id && t.RequiresEDocument);
        _added.Single(t => t.TemplateId == a.Id).Importance.ShouldBe(OnboardingImportance.Critical);
    }

    [Fact]
    public async Task Cannot_start_same_set_twice_for_employee()
    {
        var a = Template("Sözleşme", null);
        var set = OnboardingTemplateSet.Create(TenantId, OnboardingStage.Onboarding, "Ofis", [a.Id]).Value;
        _sets.GetByIdAsync(set.Id, Arg.Any<CancellationToken>()).Returns(set);
        _templates.ListAsync(Arg.Any<CancellationToken>()).Returns([a]);
        _tasks.AnyForSetAsync(EmployeeId, set.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().Handle(new StartProcessCommand(EmployeeId, set.Id, Reference), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.TemplateSetAlreadyStarted);
        _added.ShouldBeEmpty();
    }

    [Fact]
    public void Category_progress_counts_done_over_total_excluding_waived()
    {
        var category = OnboardingCategory.Create(TenantId, "Belgeler", 1).Value;
        EmployeeOnboardingTask Task(Guid? cat) => EmployeeOnboardingTask.Materialize(
            TenantId, EmployeeId, Guid.NewGuid(), OnboardingStage.Onboarding, Reference, cat);
        var done = Task(category.Id);
        done.Complete(Guid.NewGuid());
        var waived = Task(category.Id);
        waived.Waive("gerek yok", Guid.NewGuid());
        var progress = OnboardingCategoryProgress.Compute([done, waived, Task(category.Id), Task(null)], [category]);

        var docs = progress.Single(p => p.CategoryId == category.Id);
        (docs.Done, docs.Total, docs.Percent).ShouldBe((1, 2, 50));
        var uncategorized = progress.Single(p => p.CategoryId is null);
        (uncategorized.Done, uncategorized.Total).ShouldBe((0, 1));
    }

    [Fact]
    public async Task Existing_templates_without_category_still_materialize()
    {
        var legacy = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "Eski", "hr", 2, true).Value;
        _templates.ListActiveByStageAsync(OnboardingStage.Onboarding, Arg.Any<CancellationToken>()).Returns([legacy]);

        var created = await OnboardingTaskMaterializer.MaterializeAsync(
            TenantId, _templates, _tasks, _outbox, EmployeeId, OnboardingStage.Onboarding, Reference, CancellationToken.None);

        var task = created.ShouldHaveSingleItem();
        task.CategoryId.ShouldBeNull();
        task.Importance.ShouldBe(OnboardingImportance.Normal);
        task.DueDate.ShouldBe(Reference.AddDays(2));
        task.TemplateSetId.ShouldBeNull();
    }
}
