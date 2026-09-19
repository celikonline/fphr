using FluentValidation;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
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

internal static class LedgerMapper
{
    public static IReadOnlyList<LeaveLedgerEntryDto> ToDtos(IEnumerable<LeaveLedgerEntry> rows) =>
        rows.Select(e => new LeaveLedgerEntryDto(e.Id, e.LeaveTypeId, e.Type, e.Amount, e.EffectiveDate, e.Source, e.SourceRefId, e.Reason, e.CreatedAt, e.CreatedUserId))
            .ToList();
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
        return Result.Success(LedgerMapper.ToDtos(rows));
    }
}

/// <summary>Self-service: the caller's own ledger rows ("Detaylar" on my leave card).</summary>
[RequiresPermission("leave.my.balances")]
public sealed record ListMyLeaveLedgerQuery(Guid? LeaveTypeId) : IQuery<IReadOnlyList<LeaveLedgerEntryDto>>;

public sealed class ListMyLeaveLedgerHandler(ICurrentUser currentUser, ILeaveLedgerRepository ledger)
    : IQueryHandler<ListMyLeaveLedgerQuery, IReadOnlyList<LeaveLedgerEntryDto>>
{
    public async Task<Result<IReadOnlyList<LeaveLedgerEntryDto>>> Handle(ListMyLeaveLedgerQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var rows = await ledger.ListAsync(employeeId, query.LeaveTypeId, cancellationToken);
        return Result.Success(LedgerMapper.ToDtos(rows));
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

/// <summary>
/// senseik-036 md.7 (4857 md.25/I-b): trailing 12-month Sick-type usage total + whether it has reached the
/// six-week informational floor. See <see cref="SickLeaveAbsenceThreshold"/> for why this is a caveated signal,
/// not a legal determination (no employee-specific notice period is available to Leave yet).
/// </summary>
public sealed record SickLeaveAbsenceStatusDto(decimal UsedDaysLast12Months, bool ThresholdReached);

internal static class SickLeaveAbsenceStatusCalculator
{
    private const int WindowDays = 365;

    public static async Task<SickLeaveAbsenceStatusDto> ComputeAsync(
        LeaveRuleSet set, Guid employeeId, DateOnly today, ILeaveLedgerRepository ledger, CancellationToken cancellationToken)
    {
        var sickType = set.Types.FirstOrDefault(t => t.Code == LeaveTypeCode.Sick);
        if (sickType is null)
        {
            return new SickLeaveAbsenceStatusDto(0m, false);
        }

        var windowStart = today.AddDays(-WindowDays);
        var entries = await ledger.ListAsync(employeeId, sickType.Id, cancellationToken);
        // Usage rows are negative and Reversal rows (cancelled leave) positive at the same EffectiveDate, so
        // summing and negating nets cancellations out automatically.
        var used = -entries.Where(e => e.EffectiveDate >= windowStart && e.EffectiveDate <= today).Sum(e => e.Amount);
        return new SickLeaveAbsenceStatusDto(used, SickLeaveAbsenceThreshold.IsReached(used));
    }
}

/// <summary>İK: bir çalışanın hastalık izni devamsızlık eşiği durumu.</summary>
[RequiresPermission("leave.balances.read")]
public sealed record GetEmployeeSickLeaveAbsenceStatusQuery(Guid EmployeeId) : IQuery<SickLeaveAbsenceStatusDto>;

public sealed class GetEmployeeSickLeaveAbsenceStatusHandler(
    IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope, ILeaveLedgerRepository ledger, LeaveBalanceService balances)
    : IQueryHandler<GetEmployeeSickLeaveAbsenceStatusQuery, SickLeaveAbsenceStatusDto>
{
    public async Task<Result<SickLeaveAbsenceStatusDto>> Handle(GetEmployeeSickLeaveAbsenceStatusQuery query, CancellationToken cancellationToken)
    {
        var loaded = await new EmployeeLeaveContext(profiles, ruleSets, dataScope).LoadAsync(query.EmployeeId, LeavePermissions.BalancesRead.Key, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (profile, set) = loaded.Value;
        return await SickLeaveAbsenceStatusCalculator.ComputeAsync(set, profile.EmployeeId, balances.Today, ledger, cancellationToken);
    }
}

/// <summary>Self-service: the caller's own hastalık izni devamsızlık eşiği durumu.</summary>
[RequiresPermission("leave.my.balances")]
public sealed record GetMySickLeaveAbsenceStatusQuery : IQuery<SickLeaveAbsenceStatusDto>;

public sealed class GetMySickLeaveAbsenceStatusHandler(
    ICurrentUser currentUser, IEmployeeLeaveProfileRepository profiles, ILeaveRuleSetRepository ruleSets, IDataScopeService dataScope,
    ILeaveLedgerRepository ledger, LeaveBalanceService balances)
    : IQueryHandler<GetMySickLeaveAbsenceStatusQuery, SickLeaveAbsenceStatusDto>
{
    public async Task<Result<SickLeaveAbsenceStatusDto>> Handle(GetMySickLeaveAbsenceStatusQuery query, CancellationToken cancellationToken)
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
        return await SickLeaveAbsenceStatusCalculator.ComputeAsync(set, profile.EmployeeId, balances.Today, ledger, cancellationToken);
    }
}
