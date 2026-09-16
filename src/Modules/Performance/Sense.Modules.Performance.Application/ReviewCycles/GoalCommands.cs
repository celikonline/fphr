using FluentValidation;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record GoalScaleRangeDto(decimal Min, decimal Max, decimal Points);

/// <summary>
/// "Hedef ekle". GoalEntryBy=Employee'de yalnız katılımcının kendisi, FirstManager'da yalnız yöneticisi ekleyebilir -
/// ParticipantEmployeeId, IScopedRequest ile Manager'ı (DataScope.Team) yalnız kendi ekibine, Employee'yi (DataScope.Self)
/// yalnız kendisine kısıtlar; GoalEntryMode kontrolü ise o rolün BU döngüde doğru taraf olup olmadığını ayrıca doğrular.
/// </summary>
[RequiresPermission("performance.goals.create")]
public sealed record AddGoalCommand(
    Guid ReviewCycleId, Guid ParticipantId, Guid ParticipantEmployeeId, Guid CategoryId, string Name, string? Description,
    decimal TargetValue, IReadOnlyList<GoalScaleRangeDto> Scale, decimal? Weight, Guid ActorEmployeeId) : ICommand<Guid>, IScopedRequest
{
    public Guid? TargetEmployeeId => ParticipantEmployeeId;
}

public sealed class AddGoalValidator : AbstractValidator<AddGoalCommand>
{
    public AddGoalValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.ParticipantId).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(PerformanceLimits.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(PerformanceLimits.TextMaxLength);
        RuleFor(x => x.Scale).NotNull().NotEmpty();
    }
}

public sealed class AddGoalHandler(IReviewCycleRepository cycles) : ICommandHandler<AddGoalCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddGoalCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var cycle = existing.Value;
        var modeCheck = GoalEntryModeGuard.Check(cycle, command.ParticipantEmployeeId, command.ActorEmployeeId);
        if (modeCheck is not null)
        {
            return modeCheck;
        }

        var scale = command.Scale.Select(s => new GoalScaleRange(s.Min, s.Max, s.Points)).ToList();
        var result = cycle.AddGoal(
            command.ParticipantId, command.CategoryId, command.Name, command.Description, command.TargetValue,
            scale, command.Weight, command.ActorEmployeeId);
        return result;
    }
}

/// <summary>"Hedefleri onayla": her zaman katılımcının yöneticisi (ya da HR/Owner/Admin) - IScopedRequest ile kısıtlanır.</summary>
[RequiresPermission("performance.goals.approve")]
public sealed record ApproveGoalsCommand(Guid ReviewCycleId, Guid ParticipantId, Guid ParticipantEmployeeId, Guid ActorEmployeeId) : ICommand, IScopedRequest
{
    public Guid? TargetEmployeeId => ParticipantEmployeeId;
}

public sealed class ApproveGoalsValidator : AbstractValidator<ApproveGoalsCommand>
{
    public ApproveGoalsValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.ParticipantId).NotEmpty();
    }
}

public sealed class ApproveGoalsHandler(IReviewCycleRepository cycles) : ICommandHandler<ApproveGoalsCommand>
{
    public async Task<Result> Handle(ApproveGoalsCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.ApproveGoals(command.ParticipantId, command.ActorEmployeeId);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>"Sonuç gir": aynı GoalEntryMode kuralı - yalnız katılımcı (Employee modu) ya da yöneticisi (FirstManager modu).</summary>
[RequiresPermission("performance.goals.enter_result")]
public sealed record EnterGoalResultCommand(
    Guid ReviewCycleId, Guid GoalId, Guid ParticipantEmployeeId, decimal ActualValue, string? ResultComment, Guid ActorEmployeeId)
    : ICommand, IScopedRequest
{
    public Guid? TargetEmployeeId => ParticipantEmployeeId;
}

public sealed class EnterGoalResultValidator : AbstractValidator<EnterGoalResultCommand>
{
    public EnterGoalResultValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.GoalId).NotEmpty();
        RuleFor(x => x.ResultComment).MaximumLength(PerformanceLimits.CommentMaxLength);
    }
}

public sealed class EnterGoalResultHandler(IReviewCycleRepository cycles) : ICommandHandler<EnterGoalResultCommand>
{
    public async Task<Result> Handle(EnterGoalResultCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var cycle = existing.Value;
        var modeCheck = GoalEntryModeGuard.Check(cycle, command.ParticipantEmployeeId, command.ActorEmployeeId);
        if (modeCheck is not null)
        {
            return modeCheck;
        }

        var result = cycle.EnterGoalResult(command.GoalId, command.ActualValue, command.ResultComment, command.ActorEmployeeId);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>"Sonuçları onayla": her zaman katılımcının yöneticisi (ya da HR/Owner/Admin).</summary>
[RequiresPermission("performance.goals.approve_result")]
public sealed record ApproveGoalResultsCommand(Guid ReviewCycleId, Guid ParticipantId, Guid ParticipantEmployeeId, Guid ActorEmployeeId) : ICommand, IScopedRequest
{
    public Guid? TargetEmployeeId => ParticipantEmployeeId;
}

public sealed class ApproveGoalResultsValidator : AbstractValidator<ApproveGoalResultsCommand>
{
    public ApproveGoalResultsValidator()
    {
        RuleFor(x => x.ReviewCycleId).NotEmpty();
        RuleFor(x => x.ParticipantId).NotEmpty();
    }
}

public sealed class ApproveGoalResultsHandler(IReviewCycleRepository cycles, TimeProvider clock) : ICommandHandler<ApproveGoalResultsCommand>
{
    public async Task<Result> Handle(ApproveGoalResultsCommand command, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(command.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.ApproveGoalResults(command.ParticipantId, command.ActorEmployeeId, clock.GetUtcNow().UtcDateTime);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>ReviewCycle.GoalEntryBy'a göre yazma yetkisi: Employee modunda yalnız katılımcının kendisi, FirstManager modunda yalnız başka biri (yöneticisi - Team scope zaten bunu doğrular).</summary>
internal static class GoalEntryModeGuard
{
    public static Error? Check(ReviewCycle cycle, Guid participantEmployeeId, Guid actorEmployeeId)
    {
        var actorIsParticipant = actorEmployeeId == participantEmployeeId;
        var expectSelfEntry = cycle.GoalEntryBy == GoalEntryBy.Employee;
        return actorIsParticipant == expectSelfEntry ? null : Error.Forbidden(PerformanceErrors.GoalEntryModeMismatch);
    }
}
