using FluentValidation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Unions;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Unions;

public sealed record UnionDto(Guid Id, string Name, string? IndustryBranchCode, bool IsActive);

internal static class UnionMapper
{
    public static UnionDto ToDto(Union union) => new(union.Id, union.Name, union.IndustryBranchCode, union.IsActive);
}

/// <summary>Every union defined for the tenant (11.3.07-bordro.md "Sendika & TİS" screen's union list, #58).</summary>
[RequiresPermission("payroll.union.manage")]
public sealed record ListUnionsQuery : IQuery<IReadOnlyList<UnionDto>>;

public sealed class ListUnionsHandler(IUnionRepository unions) : IQueryHandler<ListUnionsQuery, IReadOnlyList<UnionDto>>
{
    public async Task<Result<IReadOnlyList<UnionDto>>> Handle(ListUnionsQuery query, CancellationToken cancellationToken) =>
        (await unions.ListAsync(cancellationToken)).Select(UnionMapper.ToDto).ToList();
}

[RequiresPermission("payroll.union.manage")]
public sealed record CreateUnionCommand(string Name, string? IndustryBranchCode) : ICommand<Guid>;

public sealed class CreateUnionValidator : AbstractValidator<CreateUnionCommand>
{
    public CreateUnionValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed class CreateUnionHandler(ITenantContext tenant, IUnionRepository unions) : ICommandHandler<CreateUnionCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateUnionCommand command, CancellationToken cancellationToken)
    {
        var created = Union.Create(tenant.TenantId, command.Name, command.IndustryBranchCode);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<Guid>>(created.Error);
        }

        unions.Add(created.Value);
        return Task.FromResult<Result<Guid>>(created.Value.Id);
    }
}

[RequiresPermission("payroll.union.manage")]
public sealed record UpdateUnionCommand(Guid UnionId, string Name, string? IndustryBranchCode) : ICommand;

public sealed class UpdateUnionHandler(IUnionRepository unions) : ICommandHandler<UpdateUnionCommand>
{
    public async Task<Result> Handle(UpdateUnionCommand command, CancellationToken cancellationToken)
    {
        var union = await unions.FindByIdAsync(command.UnionId, cancellationToken);
        return union is null ? Error.NotFound(PayrollDomainErrors.UnionNotFound) : union.Update(command.Name, command.IndustryBranchCode);
    }
}

[RequiresPermission("payroll.union.manage")]
public sealed record DeactivateUnionCommand(Guid UnionId) : ICommand;

public sealed class DeactivateUnionHandler(IUnionRepository unions) : ICommandHandler<DeactivateUnionCommand>
{
    public async Task<Result> Handle(DeactivateUnionCommand command, CancellationToken cancellationToken)
    {
        var union = await unions.FindByIdAsync(command.UnionId, cancellationToken);
        if (union is null)
        {
            return Error.NotFound(PayrollDomainErrors.UnionNotFound);
        }

        union.Deactivate();
        return Result.Success();
    }
}
