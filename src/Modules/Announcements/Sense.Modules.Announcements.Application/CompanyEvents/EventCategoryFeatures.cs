using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.CompanyEvents;

public sealed record CompanyEventCategoryDto(Guid Id, string Name, string Color);

/// <summary>Company event category catalog (E4).</summary>
[RequiresPermission("announcements.my.events")]
public sealed record ListCompanyEventCategoriesQuery : IQuery<IReadOnlyList<CompanyEventCategoryDto>>;

public sealed class ListCompanyEventCategoriesHandler(ITenantContext tenant, ICompanyEventCategoryRepository categories)
    : IQueryHandler<ListCompanyEventCategoriesQuery, IReadOnlyList<CompanyEventCategoryDto>>
{
    public async Task<Result<IReadOnlyList<CompanyEventCategoryDto>>> Handle(
        ListCompanyEventCategoriesQuery query, CancellationToken cancellationToken)
    {
        var all = await categories.ListAsync(tenant.TenantId, cancellationToken);
        return Result.Success<IReadOnlyList<CompanyEventCategoryDto>>(
            all.Select(c => new CompanyEventCategoryDto(c.Id, c.Name, c.Color)).ToList());
    }
}

[RequiresPermission("announcements.events.manage")]
public sealed record CreateCompanyEventCategoryCommand(string Name, string Color) : ICommand<Guid>;

public sealed class CreateCompanyEventCategoryHandler(ITenantContext tenant, ICompanyEventCategoryRepository categories)
    : ICommandHandler<CreateCompanyEventCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCompanyEventCategoryCommand command, CancellationToken cancellationToken)
    {
        var created = CompanyEventCategory.Create(tenant.TenantId, command.Name, command.Color);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var existing = await categories.ListAsync(tenant.TenantId, cancellationToken);
        if (existing.Any(c => string.Equals(c.Name, created.Value.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Conflict(CompanyEventDomainErrors.CategoryDuplicateName);
        }

        categories.Add(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("announcements.events.manage")]
public sealed record UpdateCompanyEventCategoryCommand(Guid CategoryId, string Name, string Color) : ICommand;

public sealed class UpdateCompanyEventCategoryHandler(ITenantContext tenant, ICompanyEventCategoryRepository categories)
    : ICommandHandler<UpdateCompanyEventCategoryCommand>
{
    public async Task<Result> Handle(UpdateCompanyEventCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await categories.GetByIdAsync(tenant.TenantId, command.CategoryId, cancellationToken);
        return category is null
            ? Error.NotFound(CompanyEventDomainErrors.CategoryNotFound)
            : category.Update(command.Name, command.Color);
    }
}

[RequiresPermission("announcements.events.manage")]
public sealed record DeleteCompanyEventCategoryCommand(Guid CategoryId) : ICommand;

public sealed class DeleteCompanyEventCategoryHandler(ITenantContext tenant, ICompanyEventCategoryRepository categories)
    : ICommandHandler<DeleteCompanyEventCategoryCommand>
{
    public async Task<Result> Handle(DeleteCompanyEventCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await categories.GetByIdAsync(tenant.TenantId, command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Error.NotFound(CompanyEventDomainErrors.CategoryNotFound);
        }

        if (await categories.IsInUseAsync(tenant.TenantId, category.Id, cancellationToken))
        {
            return Error.Conflict(CompanyEventDomainErrors.CategoryInUse);
        }

        categories.Remove(category);
        return Result.Success();
    }
}
