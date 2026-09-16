using FluentValidation;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

/// <summary>Creates a review cycle (dönem) in Draft status.</summary>
[RequiresPermission("performance.reviews.manage")]
public sealed record CreateReviewCycleCommand(string Name, DateOnly StartDate, DateOnly EndDate) : ICommand<Guid>;

public sealed class CreateReviewCycleValidator : AbstractValidator<CreateReviewCycleCommand>
{
    public CreateReviewCycleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate);
    }
}

public sealed class CreateReviewCycleHandler(ITenantContext tenant, IReviewCycleRepository cycles) : ICommandHandler<CreateReviewCycleCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateReviewCycleCommand command, CancellationToken cancellationToken)
    {
        var created = ReviewCycle.Create(tenant.TenantId, command.Name, command.StartDate, command.EndDate);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        cycles.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

/// <summary>Draft -> Active: opens the cycle for review form authoring/goal linkage.</summary>
[RequiresPermission("performance.reviews.manage")]
public sealed record ActivateReviewCycleCommand(Guid ReviewCycleId) : ICommand;

public sealed class ActivateReviewCycleHandler(IReviewCycleRepository cycles) : ICommandHandler<ActivateReviewCycleCommand>
{
    public async Task<Result> Handle(ActivateReviewCycleCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Activate();
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Closes a review cycle - terminal, finalizes ("approves") everything recorded in it. Higher authority than
/// Create/Activate (performance.reviews.approve, not .manage) - see rapor for the immutability rationale.</summary>
[RequiresPermission("performance.reviews.approve")]
public sealed record CloseReviewCycleCommand(Guid ReviewCycleId) : ICommand;

public sealed class CloseReviewCycleHandler(IReviewCycleRepository cycles) : ICommandHandler<CloseReviewCycleCommand>
{
    public async Task<Result> Handle(CloseReviewCycleCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Close();
        return result.IsFailure ? result.Error : Result.Success();
    }
}
