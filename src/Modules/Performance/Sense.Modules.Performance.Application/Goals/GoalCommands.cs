using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.Goals;

/// <summary>
/// G-8-19/G-8-20 ortak kapı: bir hedef bir değerlendirme dönemine bağlıysa (ReviewCycleId dolu), dönem kapatılmışken
/// hiçbir değişiklik (oluşturma dahil) kabul edilmez - 4857 md.18 fesih delili gerekçesi, PublishReviewFormHandler'daki
/// kalıp (bkz. ReviewFormCommands.cs). Karar (a): dönem bulunamazsa mevcut PerformanceDomainErrors.CycleNotFound
/// döner, yeni not-found kodu açılmaz. Oluşturmada ayrıca TargetDate'in [StartDate, EndDate] aralığında olması
/// zorunludur (G-8-20).
/// </summary>
internal static class GoalReviewCycleGuard
{
    public static async Task<Error?> EnsureOpenAsync(IReviewCycleRepository cycles, Guid reviewCycleId, CancellationToken cancellationToken)
    {
        var cycle = await cycles.GetByIdAsync(reviewCycleId, cancellationToken);
        if (cycle.IsFailure)
        {
            return cycle.Error;
        }

        return cycle.Value.Status == ReviewCycleStatus.Closed ? Error.Rule(PerformanceErrors.ReviewImmutable) : null;
    }

    public static async Task<Error?> EnsureOpenAndInRangeAsync(IReviewCycleRepository cycles, Guid reviewCycleId, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var cycle = await cycles.GetByIdAsync(reviewCycleId, cancellationToken);
        if (cycle.IsFailure)
        {
            return cycle.Error;
        }

        if (cycle.Value.Status == ReviewCycleStatus.Closed)
        {
            return Error.Rule(PerformanceErrors.ReviewImmutable);
        }

        return targetDate < cycle.Value.StartDate || targetDate > cycle.Value.EndDate
            ? Error.Validation(PerformanceErrors.GoalTargetDateOutsideCycle)
            : null;
    }
}

/// <summary>HR/manager creates a goal for an employee (Manager is scoped to their team by "performance.goals.*" @Team - see SystemRoleDefinitions.cs).</summary>
[RequiresPermission("performance.goals.manage")]
public sealed record CreateGoalCommand(Guid EmployeeId, string Title, string? Description, DateOnly TargetDate, Guid? ReviewCycleId) : ICommand<Guid>;

public sealed class CreateGoalValidator : AbstractValidator<CreateGoalCommand>
{
    public CreateGoalValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
    }
}

public sealed class CreateGoalHandler(ITenantContext tenant, IEmployeeLookup employees, IGoalRepository goals, IReviewCycleRepository cycles)
    : ICommandHandler<CreateGoalCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGoalCommand command, CancellationToken cancellationToken)
    {
        if (!await employees.ExistsActiveAsync(tenant.TenantId, command.EmployeeId, cancellationToken))
        {
            return Error.Validation(PerformanceErrors.EmployeeInvalid);
        }

        if (command.ReviewCycleId is { } reviewCycleId)
        {
            var cycleError = await GoalReviewCycleGuard.EnsureOpenAndInRangeAsync(cycles, reviewCycleId, command.TargetDate, cancellationToken);
            if (cycleError is not null)
            {
                return cycleError;
            }
        }

        var created = Goal.Create(tenant.TenantId, command.EmployeeId, command.Title, command.Description, command.TargetDate, command.ReviewCycleId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        goals.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>Self-service: an employee sets their own goal (e.g. after a 1:1 discussion with their manager).</summary>
[RequiresPermission("performance.my.goals")]
public sealed record CreateMyGoalCommand(string Title, string? Description, DateOnly TargetDate, Guid? ReviewCycleId) : ICommand<Guid>;

public sealed class CreateMyGoalValidator : AbstractValidator<CreateMyGoalCommand>
{
    public CreateMyGoalValidator() => RuleFor(x => x.Title).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
}

public sealed class CreateMyGoalHandler(ITenantContext tenant, ICurrentUser currentUser, IGoalRepository goals, IReviewCycleRepository cycles)
    : ICommandHandler<CreateMyGoalCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMyGoalCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.Validation(PerformanceErrors.NoEmployeeForUser);
        }

        if (command.ReviewCycleId is { } reviewCycleId)
        {
            var cycleError = await GoalReviewCycleGuard.EnsureOpenAndInRangeAsync(cycles, reviewCycleId, command.TargetDate, cancellationToken);
            if (cycleError is not null)
            {
                return cycleError;
            }
        }

        var created = Goal.Create(tenant.TenantId, employeeId, command.Title, command.Description, command.TargetDate, command.ReviewCycleId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        goals.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>HR/manager updates any (in-scope) employee's goal progress.</summary>
[RequiresPermission("performance.goals.manage")]
public sealed record UpdateGoalProgressCommand(Guid GoalId, int Progress) : ICommand;

public sealed class UpdateGoalProgressHandler(IGoalRepository goals, IReviewCycleRepository cycles) : ICommandHandler<UpdateGoalProgressCommand>
{
    public async Task<Result> Handle(UpdateGoalProgressCommand command, CancellationToken cancellationToken)
    {
        var existing = await goals.GetByIdAsync(command.GoalId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.ReviewCycleId is { } reviewCycleId)
        {
            var cycleError = await GoalReviewCycleGuard.EnsureOpenAsync(cycles, reviewCycleId, cancellationToken);
            if (cycleError is not null)
            {
                return cycleError;
            }
        }

        var result = existing.Value.UpdateProgress(command.Progress);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Self-service progress update; rejected if the goal does not belong to the caller (HR/manager use <see cref="UpdateGoalProgressCommand"/> instead).</summary>
[RequiresPermission("performance.my.goals")]
public sealed record UpdateMyGoalProgressCommand(Guid GoalId, int Progress) : ICommand;

/// <summary>Karar (b): öz-servis kontrol sırası sahiplik (403) → kapalı dönem (409) - önce 403 döner, dönem durumu
/// başkasının hedefine bakan bir çağırana sızdırılmaz.</summary>
public sealed class UpdateMyGoalProgressHandler(ICurrentUser currentUser, IGoalRepository goals, IReviewCycleRepository cycles)
    : ICommandHandler<UpdateMyGoalProgressCommand>
{
    public async Task<Result> Handle(UpdateMyGoalProgressCommand command, CancellationToken cancellationToken)
    {
        var existing = await goals.GetByIdAsync(command.GoalId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.EmployeeId != currentUser.EmployeeId)
        {
            return Error.Forbidden(ErrorCodes.Forbidden);
        }

        if (existing.Value.ReviewCycleId is { } reviewCycleId)
        {
            var cycleError = await GoalReviewCycleGuard.EnsureOpenAsync(cycles, reviewCycleId, cancellationToken);
            if (cycleError is not null)
            {
                return cycleError;
            }
        }

        var result = existing.Value.UpdateProgress(command.Progress);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>HR/manager cancels a goal.</summary>
[RequiresPermission("performance.goals.manage")]
public sealed record CancelGoalCommand(Guid GoalId) : ICommand;

public sealed class CancelGoalHandler(IGoalRepository goals, IReviewCycleRepository cycles) : ICommandHandler<CancelGoalCommand>
{
    public async Task<Result> Handle(CancelGoalCommand command, CancellationToken cancellationToken)
    {
        var existing = await goals.GetByIdAsync(command.GoalId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (existing.Value.ReviewCycleId is { } reviewCycleId)
        {
            var cycleError = await GoalReviewCycleGuard.EnsureOpenAsync(cycles, reviewCycleId, cancellationToken);
            if (cycleError is not null)
            {
                return cycleError;
            }
        }

        var result = existing.Value.Cancel();
        return result.IsFailure ? result.Error : Result.Success();
    }
}
