using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.Goals;

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

public sealed class CreateGoalHandler(ITenantContext tenant, IEmployeeLookup employees, IGoalRepository goals) : ICommandHandler<CreateGoalCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGoalCommand command, CancellationToken cancellationToken)
    {
        if (!await employees.ExistsActiveAsync(tenant.TenantId, command.EmployeeId, cancellationToken))
        {
            return Error.Validation(PerformanceErrors.EmployeeInvalid);
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

public sealed class CreateMyGoalHandler(ITenantContext tenant, ICurrentUser currentUser, IGoalRepository goals) : ICommandHandler<CreateMyGoalCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateMyGoalCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Task.FromResult(Result.Failure<Guid>(Error.Validation(PerformanceErrors.NoEmployeeForUser)));
        }

        var created = Goal.Create(tenant.TenantId, employeeId, command.Title, command.Description, command.TargetDate, command.ReviewCycleId);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        goals.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

/// <summary>HR/manager updates any (in-scope) employee's goal progress.</summary>
[RequiresPermission("performance.goals.manage")]
public sealed record UpdateGoalProgressCommand(Guid GoalId, int Progress) : ICommand;

public sealed class UpdateGoalProgressHandler(IGoalRepository goals) : ICommandHandler<UpdateGoalProgressCommand>
{
    public async Task<Result> Handle(UpdateGoalProgressCommand command, CancellationToken cancellationToken)
    {
        var existing = await goals.GetByIdAsync(command.GoalId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.UpdateProgress(command.Progress);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Self-service progress update; rejected if the goal does not belong to the caller (HR/manager use <see cref="UpdateGoalProgressCommand"/> instead).</summary>
[RequiresPermission("performance.my.goals")]
public sealed record UpdateMyGoalProgressCommand(Guid GoalId, int Progress) : ICommand;

public sealed class UpdateMyGoalProgressHandler(ICurrentUser currentUser, IGoalRepository goals) : ICommandHandler<UpdateMyGoalProgressCommand>
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

        var result = existing.Value.UpdateProgress(command.Progress);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>HR/manager cancels a goal.</summary>
[RequiresPermission("performance.goals.manage")]
public sealed record CancelGoalCommand(Guid GoalId) : ICommand;

public sealed class CancelGoalHandler(IGoalRepository goals) : ICommandHandler<CancelGoalCommand>
{
    public async Task<Result> Handle(CancelGoalCommand command, CancellationToken cancellationToken)
    {
        var existing = await goals.GetByIdAsync(command.GoalId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Cancel();
        return result.IsFailure ? result.Error : Result.Success();
    }
}
