using FluentValidation;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.RuleSets;

public sealed record LeaveTypeDto(Guid Id, int SortOrder, LeaveTypeSettings Settings);

public sealed record LeaveRuleSetDto(
    Guid Id, string Name, bool IsDefault, bool IsSystemSeed, bool UnpaidLeaveShiftsAccrual, int AssignedEmployeeCount, IReadOnlyList<LeaveTypeDto> Types);

internal static class LeaveRuleSetMapper
{
    public static LeaveRuleSetDto ToDto(LeaveRuleSet set, int assigned) =>
        new(set.Id, set.Name, set.IsDefault, set.IsSystemSeed, set.UnpaidLeaveShiftsAccrual, assigned,
            set.Types.OrderBy(t => t.SortOrder).Select(t => new LeaveTypeDto(t.Id, t.SortOrder, t.ToSettings())).ToList());
}

/// <summary>Rule sets with their types; seeds the tenant's default set on first read.</summary>
[RequiresPermission("leave.rule_sets.read")]
public sealed record ListLeaveRuleSetsQuery : IQuery<IReadOnlyList<LeaveRuleSetDto>>;

public sealed class ListLeaveRuleSetsHandler(
    ITenantContext tenant, ILeaveRuleSetRepository ruleSets, Balances.LeaveBalanceService balances, ILeaveUnitOfWork unitOfWork)
    : IQueryHandler<ListLeaveRuleSetsQuery, IReadOnlyList<LeaveRuleSetDto>>
{
    public async Task<Result<IReadOnlyList<LeaveRuleSetDto>>> Handle(ListLeaveRuleSetsQuery query, CancellationToken cancellationToken)
    {
        if (await ruleSets.FindDefaultAsync(cancellationToken) is null)
        {
            await balances.EnsureDefaultRuleSetAsync(tenant.TenantId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var list = await ruleSets.ListAsync(cancellationToken);
        var result = new List<LeaveRuleSetDto>(list.Count);
        foreach (var set in list)
        {
            result.Add(LeaveRuleSetMapper.ToDto(set, await ruleSets.CountAssignedEmployeesAsync(set.Id, cancellationToken)));
        }

        return result;
    }
}

[RequiresPermission("leave.rule_sets.read")]
public sealed record GetLeaveRuleSetQuery(Guid RuleSetId) : IQuery<LeaveRuleSetDto>;

public sealed class GetLeaveRuleSetHandler(ILeaveRuleSetRepository ruleSets) : IQueryHandler<GetLeaveRuleSetQuery, LeaveRuleSetDto>
{
    public async Task<Result<LeaveRuleSetDto>> Handle(GetLeaveRuleSetQuery query, CancellationToken cancellationToken)
    {
        var set = await ruleSets.GetByIdAsync(query.RuleSetId, cancellationToken);
        return set.IsFailure ? set.Error : LeaveRuleSetMapper.ToDto(set.Value, await ruleSets.CountAssignedEmployeesAsync(set.Value.Id, cancellationToken));
    }
}

/// <summary>"Kopyala ve düzenle": deep copy of a rule set (types + tiers) under a new name.</summary>
[RequiresPermission("leave.rule_sets.manage")]
public sealed record CloneLeaveRuleSetCommand(Guid SourceRuleSetId, string Name) : ICommand<Guid>;

public sealed class CloneLeaveRuleSetValidator : AbstractValidator<CloneLeaveRuleSetCommand>
{
    public CloneLeaveRuleSetValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(LeaveLimits.NameMaxLength);
}

public sealed class CloneLeaveRuleSetHandler(ILeaveRuleSetRepository ruleSets) : ICommandHandler<CloneLeaveRuleSetCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CloneLeaveRuleSetCommand command, CancellationToken cancellationToken)
    {
        var source = await ruleSets.GetByIdAsync(command.SourceRuleSetId, cancellationToken);
        if (source.IsFailure)
        {
            return source.Error;
        }

        var clone = source.Value.Clone(command.Name);
        if (clone.IsFailure)
        {
            return clone.Error;
        }

        ruleSets.Add(clone.Value);
        return clone.Value.Id;
    }
}

[RequiresPermission("leave.types.manage")]
public sealed record AddLeaveTypeCommand(Guid RuleSetId, LeaveTypeSettings Settings) : ICommand<Guid>;

public sealed class AddLeaveTypeHandler(ILeaveRuleSetRepository ruleSets) : ICommandHandler<AddLeaveTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddLeaveTypeCommand command, CancellationToken cancellationToken)
    {
        var set = await ruleSets.GetByIdAsync(command.RuleSetId, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var added = set.Value.AddType(command.Settings with { IsStatutory = false });
        return added.IsFailure ? added.Error : added.Value.Id;
    }
}

[RequiresPermission("leave.types.manage")]
public sealed record UpdateLeaveTypeCommand(Guid RuleSetId, Guid LeaveTypeId, LeaveTypeSettings Settings) : ICommand;

public sealed class UpdateLeaveTypeHandler(ILeaveRuleSetRepository ruleSets) : ICommandHandler<UpdateLeaveTypeCommand>
{
    public async Task<Result> Handle(UpdateLeaveTypeCommand command, CancellationToken cancellationToken)
    {
        var set = await ruleSets.GetByIdAsync(command.RuleSetId, cancellationToken);
        return set.IsFailure ? set.Error : set.Value.UpdateType(command.LeaveTypeId, command.Settings);
    }
}

[RequiresPermission("leave.types.manage")]
public sealed record SetLeaveTypeDisabledCommand(Guid RuleSetId, Guid LeaveTypeId, bool Disabled) : ICommand;

public sealed class SetLeaveTypeDisabledHandler(ILeaveRuleSetRepository ruleSets) : ICommandHandler<SetLeaveTypeDisabledCommand>
{
    public async Task<Result> Handle(SetLeaveTypeDisabledCommand command, CancellationToken cancellationToken)
    {
        var set = await ruleSets.GetByIdAsync(command.RuleSetId, cancellationToken);
        return set.IsFailure ? set.Error : set.Value.SetTypeDisabled(command.LeaveTypeId, command.Disabled);
    }
}
