using FluentValidation;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Profiles;
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

[RequiresPermission("leave.rule_sets.manage")]
public sealed record RenameLeaveRuleSetCommand(Guid RuleSetId, string Name) : ICommand;

public sealed class RenameLeaveRuleSetValidator : AbstractValidator<RenameLeaveRuleSetCommand>
{
    public RenameLeaveRuleSetValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(LeaveLimits.NameMaxLength);
}

public sealed class RenameLeaveRuleSetHandler(ILeaveRuleSetRepository ruleSets) : ICommandHandler<RenameLeaveRuleSetCommand>
{
    public async Task<Result> Handle(RenameLeaveRuleSetCommand command, CancellationToken cancellationToken)
    {
        var set = await ruleSets.GetByIdAsync(command.RuleSetId, cancellationToken);
        return set.IsFailure ? set.Error : set.Value.Rename(command.Name);
    }
}

/// <summary>"Varsayılan yap": exactly one default rule set per tenant afterwards (new employees get it).</summary>
[RequiresPermission("leave.rule_sets.manage")]
public sealed record SetDefaultLeaveRuleSetCommand(Guid RuleSetId) : ICommand;

public sealed class SetDefaultLeaveRuleSetHandler(ILeaveRuleSetRepository ruleSets) : ICommandHandler<SetDefaultLeaveRuleSetCommand>
{
    public async Task<Result> Handle(SetDefaultLeaveRuleSetCommand command, CancellationToken cancellationToken)
    {
        var set = await ruleSets.GetByIdAsync(command.RuleSetId, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        set.Value.MakeDefault(await ruleSets.ListAsync(cancellationToken));
        return Result.Success();
    }
}

/// <summary>Deletes a rule set (with its types); not the default one and not while employees are assigned to it.</summary>
[RequiresPermission("leave.rule_sets.manage")]
public sealed record DeleteLeaveRuleSetCommand(Guid RuleSetId) : ICommand;

public sealed class DeleteLeaveRuleSetHandler(ILeaveRuleSetRepository ruleSets) : ICommandHandler<DeleteLeaveRuleSetCommand>
{
    public async Task<Result> Handle(DeleteLeaveRuleSetCommand command, CancellationToken cancellationToken)
    {
        var set = await ruleSets.GetByIdAsync(command.RuleSetId, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var deletable = set.Value.EnsureDeletable(await ruleSets.CountAssignedEmployeesAsync(set.Value.Id, cancellationToken));
        if (deletable.IsFailure)
        {
            return deletable;
        }

        ruleSets.Remove(set.Value);
        return Result.Success();
    }
}

/// <summary>One non-blocking assignment warning; <see cref="LeaveTypeName"/> is the stored name (resource key for seeded types).</summary>
public sealed record LeaveRuleSetAssignmentWarning(Guid EmployeeId, string Code, string LeaveTypeName);

public sealed record LeaveRuleSetAssignmentResult(int AssignedCount, IReadOnlyList<LeaveRuleSetAssignmentWarning> Warnings);

/// <summary>
/// Assigns a rule set to employees: the leave profile switches sets, preserved ledger rows (Usage/Reversal/ExtraGrant/Adjustment/
/// OpeningBalance) and open requests move to the counterpart type (same Code) of the new set, the old set's Accrual/CarryOver
/// rows are dropped and balances are regenerated from LeaveAccrualStartDate. Usage of a type without a counterpart stays on
/// the old type and is reported as a warning. Already-assigned employees count as assigned (idempotent).
/// </summary>
[RequiresPermission("leave.rule_sets.assign")]
public sealed record AssignLeaveRuleSetCommand(Guid RuleSetId, IReadOnlyList<Guid> EmployeeIds) : ICommand<LeaveRuleSetAssignmentResult>;

public sealed class AssignLeaveRuleSetValidator : AbstractValidator<AssignLeaveRuleSetCommand>
{
    public const int MaxEmployees = 1000;

    public AssignLeaveRuleSetValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty();
        RuleFor(x => x.EmployeeIds.Count).LessThanOrEqualTo(MaxEmployees).When(x => x.EmployeeIds is not null);
    }
}

public sealed class AssignLeaveRuleSetHandler(
    ILeaveRuleSetRepository ruleSets,
    IEmployeeLeaveProfileRepository profiles,
    ILeaveLedgerRepository ledger,
    ILeaveRequestRepository requests,
    IDataScopeService dataScope,
    Balances.LeaveBalanceService balances)
    : ICommandHandler<AssignLeaveRuleSetCommand, LeaveRuleSetAssignmentResult>
{
    public async Task<Result<LeaveRuleSetAssignmentResult>> Handle(AssignLeaveRuleSetCommand command, CancellationToken cancellationToken)
    {
        var target = await ruleSets.GetByIdAsync(command.RuleSetId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error;
        }

        var employeeIds = command.EmployeeIds.Distinct().ToList();
        var loadedProfiles = new List<EmployeeLeaveProfile>(employeeIds.Count);
        foreach (var employeeId in employeeIds)
        {
            if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RuleSetsAssign.Key, employeeId, cancellationToken))
            {
                return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
            }

            var profile = await profiles.FindByEmployeeIdAsync(employeeId, cancellationToken);
            if (profile is null)
            {
                return Error.NotFound(LeaveErrors.ProfileNotFound);
            }

            loadedProfiles.Add(profile);
        }

        var sets = new Dictionary<Guid, LeaveRuleSet?> { [target.Value.Id] = target.Value };
        var warnings = new List<LeaveRuleSetAssignmentWarning>();
        foreach (var profile in loadedProfiles)
        {
            if (profile.RuleSetId == target.Value.Id)
            {
                continue;
            }

            if (!sets.TryGetValue(profile.RuleSetId, out var previous))
            {
                var loaded = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
                previous = loaded.IsSuccess ? loaded.Value : null;
                sets[profile.RuleSetId] = previous;
            }

            if (previous is not null)
            {
                warnings.AddRange(await MoveToRuleSetAsync(profile.EmployeeId, previous, target.Value, cancellationToken));
            }

            profile.AssignRuleSet(target.Value.Id);
            await balances.RecalculateAsync(profile, target.Value, cancellationToken);
        }

        return new LeaveRuleSetAssignmentResult(loadedProfiles.Count, warnings);
    }

    private async Task<IReadOnlyList<LeaveRuleSetAssignmentWarning>> MoveToRuleSetAsync(
        Guid employeeId, LeaveRuleSet previous, LeaveRuleSet target, CancellationToken cancellationToken)
    {
        var warnings = new List<LeaveRuleSetAssignmentWarning>();
        var entries = await ledger.ListAsync(employeeId, null, cancellationToken);
        foreach (var type in previous.Types)
        {
            var rows = entries.Where(e => e.LeaveTypeId == type.Id).ToList();
            ledger.RemoveRange(rows.Where(r => r.IsGenerated));
            var counterpart = target.FindCounterpart(type);
            if (counterpart is not null)
            {
                foreach (var row in rows.Where(r => !r.IsGenerated))
                {
                    row.MoveToLeaveType(counterpart.Id);
                }
            }
            else if (rows.Where(r => r.Type is LeaveLedgerEntryType.Usage or LeaveLedgerEntryType.Reversal).Sum(r => r.Amount) < 0m)
            {
                warnings.Add(new LeaveRuleSetAssignmentWarning(employeeId, LeaveWarnings.UsageTypeMissingInRuleSet, type.Name));
            }
        }

        var typeMap = previous.Types
            .Select(t => (Old: t.Id, New: target.FindCounterpart(t)?.Id))
            .Where(m => m.New is not null)
            .ToDictionary(m => m.Old, m => m.New!.Value);
        foreach (var request in await requests.ListByEmployeeAsync(employeeId, cancellationToken))
        {
            if (request.IsActive && typeMap.TryGetValue(request.LeaveTypeId, out var newTypeId))
            {
                request.MoveToLeaveType(newTypeId);
            }
        }

        return warnings;
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
