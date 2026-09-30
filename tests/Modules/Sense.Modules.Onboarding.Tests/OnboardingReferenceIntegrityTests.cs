using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NSubstitute;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Modules.Onboarding.Infrastructure.Persistence;
using Sense.Modules.Onboarding.Infrastructure.Persistence.Migrations;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Onboarding.Tests;

/// <summary>Category / template-set reference integrity (app-level validation + schema FK).</summary>
public sealed class OnboardingReferenceIntegrityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IOnboardingCategoryRepository _categories = Substitute.For<IOnboardingCategoryRepository>();
    private readonly IOnboardingTaskTemplateRepository _templates = Substitute.For<IOnboardingTaskTemplateRepository>();
    private readonly IOnboardingTemplateSetRepository _sets = Substitute.For<IOnboardingTemplateSetRepository>();

    private ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private CreateOnboardingTaskTemplateCommand CreateWith(Guid categoryId) =>
        new(OnboardingStage.Onboarding, "Görev", "hr", 0, true, categoryId);

    [Fact]
    public async Task Create_template_with_unknown_category_fails()
    {
        var id = Guid.NewGuid();
        _categories.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Error.NotFound(OnboardingDomainErrors.CategoryNotFound));

        var result = await new CreateOnboardingTaskTemplateHandler(Tenant(), _templates, _categories)
            .Handle(CreateWith(id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("onboarding.category_not_found");
        _templates.DidNotReceive().Add(Arg.Any<OnboardingTaskTemplate>());
    }

    [Fact]
    public async Task Create_template_with_other_tenant_category_fails()
    {
        // Repositories are tenant-filtered, so another tenant's category id resolves to NotFound; a foreign
        // category object must also never be accepted as usable.
        var foreign = OnboardingCategory.Create(Guid.NewGuid(), "Başka", 0).Value;
        _categories.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(Error.NotFound(OnboardingDomainErrors.CategoryNotFound));

        var result = await new CreateOnboardingTaskTemplateHandler(Tenant(), _templates, _categories)
            .Handle(CreateWith(foreign.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OnboardingDomainErrors.CategoryNotFound);
    }

    [Fact]
    public async Task Create_template_with_inactive_category_fails_but_active_succeeds()
    {
        var inactive = OnboardingCategory.Create(TenantId, "Pasif", 0).Value;
        inactive.SetActive(false);
        var active = OnboardingCategory.Create(TenantId, "Aktif", 0).Value;
        _categories.GetByIdAsync(inactive.Id, Arg.Any<CancellationToken>()).Returns(inactive);
        _categories.GetByIdAsync(active.Id, Arg.Any<CancellationToken>()).Returns(active);
        var handler = new CreateOnboardingTaskTemplateHandler(Tenant(), _templates, _categories);

        (await handler.Handle(CreateWith(inactive.Id), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await handler.Handle(CreateWith(active.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_template_with_unknown_category_fails()
    {
        var template = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "T", "hr", 0, true).Value;
        _templates.GetByIdAsync(template.Id, Arg.Any<CancellationToken>()).Returns(template);
        var id = Guid.NewGuid();
        _categories.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Error.NotFound(OnboardingDomainErrors.CategoryNotFound));

        var result = await new UpdateOnboardingTaskTemplateHandler(_templates, _categories)
            .Handle(new UpdateOnboardingTaskTemplateCommand(template.Id, "T", "hr", 0, true, id), CancellationToken.None);

        result.Error.Code.ShouldBe(OnboardingDomainErrors.CategoryNotFound);
        template.CategoryId.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_category_in_use_is_blocked()
    {
        var category = OnboardingCategory.Create(TenantId, "Belgeler", 0).Value;
        _categories.GetByIdAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _templates.AnyUsingCategoryAsync(category.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteOnboardingCategoryHandler(_categories, _templates);

        var blocked = await handler.Handle(new DeleteOnboardingCategoryCommand(category.Id), CancellationToken.None);

        blocked.IsFailure.ShouldBeTrue();
        blocked.Error.Code.ShouldBe(OnboardingDomainErrors.CategoryInUse);
        _categories.DidNotReceive().Remove(Arg.Any<OnboardingCategory>());

        _templates.AnyUsingCategoryAsync(category.Id, Arg.Any<CancellationToken>()).Returns(false);
        (await handler.Handle(new DeleteOnboardingCategoryCommand(category.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        _categories.Received(1).Remove(category);
    }

    [Fact]
    public async Task Set_with_unknown_template_id_fails()
    {
        var known = OnboardingTaskTemplate.Create(TenantId, OnboardingStage.Onboarding, "T", "hr", 0, true).Value;
        var otherTenant = OnboardingTaskTemplate.Create(Guid.NewGuid(), OnboardingStage.Onboarding, "X", "hr", 0, true).Value;
        _templates.ListAsync(Arg.Any<CancellationToken>()).Returns([known]); // tenant-filtered list

        var handler = new CreateOnboardingTemplateSetHandler(Tenant(), _sets, _templates);
        foreach (var bad in new[] { Guid.NewGuid(), otherTenant.Id })
        {
            var result = await handler.Handle(
                new CreateOnboardingTemplateSetCommand(OnboardingStage.Onboarding, "Set", [known.Id, bad]), CancellationToken.None);
            result.Error.Code.ShouldBe(OnboardingDomainErrors.TemplateSetUnknownTemplate);
        }

        _sets.DidNotReceive().Add(Arg.Any<OnboardingTemplateSet>());
    }

    [Fact]
    public void Set_preserves_template_order()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

        var set = OnboardingTemplateSet.Create(TenantId, OnboardingStage.Onboarding, "S", ids).Value;

        set.TemplateIds.ShouldBe(ids);
        set.Update("S", ids.Reverse()).IsSuccess.ShouldBeTrue();
        set.TemplateIds.ShouldBe(ids.Reverse());
    }

    [Fact]
    public void Model_has_restrict_foreign_key_from_template_to_category()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseNpgsql("Host=localhost;Database=x").Options;
        using var context = new OnboardingDbContext(options, Tenant());

        var fk = context.Model.FindEntityType(typeof(OnboardingTaskTemplate))!.GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(OnboardingCategory));

        fk.Properties.Single().Name.ShouldBe(nameof(OnboardingTaskTemplate.CategoryId));
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
    }

    [Fact]
    public void Migration_defaults_existing_rows_importance_to_Normal()
    {
        var migration = new OnboardingCategoriesAndTemplateSets();

        var importance = migration.UpOperations.OfType<AddColumnOperation>().Where(o => o.Name == "importance").ToList();

        importance.Count.ShouldBe(2); // task_templates + employee_tasks
        importance.ShouldAllBe(o => o.IsNullable == false && (string?)o.DefaultValue == nameof(OnboardingImportance.Normal));
    }
}
