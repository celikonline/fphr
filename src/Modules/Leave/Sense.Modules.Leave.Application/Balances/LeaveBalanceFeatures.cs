using FluentValidation;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Balances;

public sealed record LeaveBalanceDto(string LeaveTypeName, LeaveTypeCode LeaveTypeCode, LeaveBalance Balance);

public sealed record EmployeeLeaveBalancesDto(
    Guid EmployeeId, Guid RuleSetId, DateOnly HireDate, DateOnly LeaveAccrualStartDate, IReadOnlyList<LeaveBalanceDto> Balances);

public sealed record LeaveLedgerEntryDto(
    Guid Id, Guid LeaveTypeId, LeaveLedgerEntryType Type, decimal Amount, DateOnly EffectiveDate, LeaveLedgerSource Source,
    Guid? SourceRefId, string? Reason, DateTime RecordedAt, Guid? RecordedByUserId);

internal sealed class EmployeeLeaveContext(
    IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope)
{
    public async Task<Result<(EmployeeLeaveProfile Profile, LeaveRuleSet RuleSet)>> LoadAsync(
        Guid employeeId, string? scopePermission, CancellationToken cancellationToken)
    {
        if (scopePermission is not null && !await dataScope.CanAccessEmployeeAsync(scopePermission, employeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var profile = await profiles.FindByEmployeeIdAsync(employeeId, cancellationToken);
        if (profile is null)
        {
            return Error.NotFound(LeaveErrors.ProfileNotFound);
        }

        var set = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
        return set.IsFailure ? set.Error : (profile, set.Value);
    }
}

internal static class BalanceMapper
{
    public static EmployeeLeaveBalancesDto ToDto(EmployeeLeaveProfile profile, LeaveRuleSet set, IReadOnlyList<LeaveBalance> balances) =>
        new(profile.EmployeeId, set.Id, profile.HireDate, profile.LeaveAccrualStartDate,
            balances.Select(b =>
            {
                var type = set.Types.First(t => t.Id == b.LeaveTypeId);
                return new LeaveBalanceDto(type.Name, type.Code, b);
            }).ToList());
}

/// <summary>PerYear balances of one employee (scoped by leave.balances.read).</summary>
[RequiresPermission("leave.balances.read")]
public sealed record GetEmployeeLeaveBalancesQuery(Guid EmployeeId) : IQuery<EmployeeLeaveBalancesDto>;

public sealed class GetEmployeeLeaveBalancesHandler(
    IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope, LeaveBalanceService balances)
    : IQueryHandler<GetEmployeeLeaveBalancesQuery, EmployeeLeaveBalancesDto>
{
    public async Task<Result<EmployeeLeaveBalancesDto>> Handle(GetEmployeeLeaveBalancesQuery query, CancellationToken cancellationToken)
    {
        var loaded = await new EmployeeLeaveContext(profiles, ruleSets, dataScope).LoadAsync(query.EmployeeId, LeavePermissions.BalancesRead.Key, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (profile, set) = loaded.Value;
        return BalanceMapper.ToDto(profile, set, await balances.ProjectAsync(profile, set, cancellationToken));
    }
}

/// <summary>Self-service: the caller's own PerYear balances.</summary>
[RequiresPermission("leave.my.balances")]
public sealed record GetMyLeaveBalancesQuery : IQuery<EmployeeLeaveBalancesDto>;

public sealed class GetMyLeaveBalancesHandler(
    ICurrentUser currentUser, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope, LeaveBalanceService balances)
    : IQueryHandler<GetMyLeaveBalancesQuery, EmployeeLeaveBalancesDto>
{
    public async Task<Result<EmployeeLeaveBalancesDto>> Handle(GetMyLeaveBalancesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var loaded = await new EmployeeLeaveContext(profiles, ruleSets, dataScope).LoadAsync(employeeId, null, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (profile, set) = loaded.Value;
        return BalanceMapper.ToDto(profile, set, await balances.ProjectAsync(profile, set, cancellationToken));
    }
}

/// <summary>Ledger rows of one employee, optionally for one type ("İzin defteri").</summary>
[RequiresPermission("leave.balances.read")]
public sealed record ListLeaveLedgerQuery(Guid EmployeeId, Guid? LeaveTypeId) : IQuery<IReadOnlyList<LeaveLedgerEntryDto>>;

public sealed class ListLeaveLedgerHandler(ILeaveLedgerRepository ledger, IDataScopeService dataScope)
    : IQueryHandler<ListLeaveLedgerQuery, IReadOnlyList<LeaveLedgerEntryDto>>
{
    public async Task<Result<IReadOnlyList<LeaveLedgerEntryDto>>> Handle(ListLeaveLedgerQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.BalancesRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var rows = await ledger.ListAsync(query.EmployeeId, query.LeaveTypeId, cancellationToken);
        IReadOnlyList<LeaveLedgerEntryDto> dtos = rows
            .Select(e => new LeaveLedgerEntryDto(e.Id, e.LeaveTypeId, e.Type, e.Amount, e.EffectiveDate, e.Source, e.SourceRefId, e.Reason, e.CreatedAt, e.CreatedUserId))
            .ToList();
        return Result.Success(dtos);
    }
}

/// <summary>"Bakiye düzeltmesi": signed manual adjustment with a mandatory reason.</summary>
[RequiresPermission("leave.balances.adjust")]
public sealed record AdjustLeaveBalanceCommand(Guid EmployeeId, Guid LeaveTypeId, decimal Days, DateOnly EffectiveDate, string Reason) : ICommand<Guid>;

public sealed class AdjustLeaveBalanceValidator : AbstractValidator<AdjustLeaveBalanceCommand>
{
    public AdjustLeaveBalanceValidator()
    {
        RuleFor(x => x.Days).NotEqual(0m).InclusiveBetween(-LeaveLimits.MaxDays, LeaveLimits.MaxDays);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(LeaveLimits.ReasonMaxLength);
    }
}

public sealed class AdjustLeaveBalanceHandler(
    ITenantContext tenant, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope, ILeaveLedgerRepository ledger)
    : ICommandHandler<AdjustLeaveBalanceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AdjustLeaveBalanceCommand command, CancellationToken cancellationToken)
    {
        var loaded = await new EmployeeLeaveContext(profiles, ruleSets, dataScope).LoadAsync(command.EmployeeId, LeavePermissions.BalancesAdjust.Key, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var type = loaded.Value.RuleSet.Types.FirstOrDefault(t => t.Id == command.LeaveTypeId);
        if (type is null)
        {
            return Error.NotFound(LeaveErrors.TypeNotFound);
        }

        if (!type.HasBalance)
        {
            return Error.Validation(LeaveErrors.TypeHasNoBalance);
        }

        var entry = LeaveLedgerEntry.Adjustment(tenant.TenantId, command.EmployeeId, type.Id, command.Days, command.EffectiveDate, command.Reason);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        ledger.AddRange([entry.Value]);
        return entry.Value.Id;
    }
}

/// <summary>Regenerates accruals and carry-overs of one employee (Usage/ExtraGrant/Adjustment rows are kept).</summary>
[RequiresPermission("leave.balances.recalculate")]
public sealed record RecalculateLeaveBalancesCommand(Guid EmployeeId) : ICommand;

public sealed class RecalculateLeaveBalancesHandler(
    IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope, LeaveBalanceService balances)
    : ICommandHandler<RecalculateLeaveBalancesCommand>
{
    public async Task<Result> Handle(RecalculateLeaveBalancesCommand command, CancellationToken cancellationToken)
    {
        var loaded = await new EmployeeLeaveContext(profiles, ruleSets, dataScope).LoadAsync(command.EmployeeId, LeavePermissions.BalancesRecalculate.Key, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        await balances.RecalculateAsync(loaded.Value.Profile, loaded.Value.RuleSet, cancellationToken);
        return Result.Success();
    }
}
